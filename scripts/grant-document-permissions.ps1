# Smoke-setup helper for Phase 2 C6a and C6b.
#
# Default mode grants the three C6a author permissions (Document.Create,
# Document.EditDraft, Document.HardDelete) to a target user by
# inserting UserPermission rows directly into the SQLite database.
# Used to prepare a fresh bootstrap install so the smoke walkthrough
# (sign in -> create document -> upload PDF -> edit metadata ->
# replace PDF -> hard-delete) has the permissions it needs.
#
# Add -CreateMultiRoleUser (C6b stop 8) to also seed the multi-role
# test user the C6b smoke walk needs to exercise the
# SignAsRoleDialog path: a `multireviewer` user assigned to BOTH the
# seeded QualityManager role AND a new `ReviewerSecondary` role,
# both of which grant Document.Review. When the user signs as a
# reviewer the role-prompter sees two eligible roles and shows the
# picker.
#
# Canonical use:
#   pwsh -File scripts/grant-document-permissions.ps1
#   pwsh -File scripts/grant-document-permissions.ps1 -CreateMultiRoleUser
#
# Optional parameters:
#   -DbPath              Override the SQLite path. Defaults to the
#                        production location:
#                        %LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db
#   -Username            Override the target user for the C6a grants.
#                        Defaults to the bootstrap Administrator user.
#   -CreateMultiRoleUser Switch — also seed the C6b multi-role test
#                        user (ReviewerSecondary role + multireviewer
#                        user with two role memberships).
#   -MultiRoleUsername   Username for the C6b test user. Default:
#                        'multireviewer'.
#   -MultiRolePassword   Plaintext password for the C6b test user.
#                        Default: 'multireviewer123' (meets the
#                        12-character minimum from PasswordPolicy).
#   -SecondaryRoleName   Name for the secondary role granting only
#                        Document.Review. Default: 'ReviewerSecondary'.
#
# Idempotent: re-running on a user/role that already exists is a
# no-op for that row. Both the C6a grant block and the C6b
# multi-role-user block check for existing rows before inserting.
#
# Prerequisites:
#   sqlite3.exe on PATH. Download from https://www.sqlite.org/download.html
#   (the precompiled "sqlite-tools-win-x64-*.zip" bundle), unzip, and
#   put sqlite3.exe somewhere on PATH. No other tooling required.
#
# IMPORTANT: This script bypasses the standard-fields interceptor and
# the audit-log interceptor (it writes via raw sqlite3 INSERT). That is
# intentional for smoke setup — these are dev-only permission grants
# that don't need to appear in the production audit trail. Production
# permission grants go through the admin UI (when it ships) which
# routes through the normal repository pipeline.

[CmdletBinding()]
param(
    [string]$DbPath = (Join-Path $env:LOCALAPPDATA 'EasySynQ\db\EasySynQ_Master.db'),
    [string]$Username = $null,
    [switch]$CreateMultiRoleUser,
    [string]$MultiRoleUsername = 'multireviewer',
    [string]$MultiRolePassword = 'multireviewer123',
    [string]$SecondaryRoleName = 'ReviewerSecondary',
    [string]$SecondReviewerUsername = 'secondreviewer',
    [string]$SecondReviewerPassword = 'secondreviewer1'
)

$ErrorActionPreference = 'Stop'

# --- Pre-flight ----------------------------------------------------

$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqlite) {
    Write-Error @"
sqlite3.exe not found on PATH.

Install: download https://www.sqlite.org/download.html
("sqlite-tools-win-x64-*.zip"), unzip, put sqlite3.exe on PATH,
then re-run this script.
"@
    exit 1
}

if (-not (Test-Path $DbPath)) {
    Write-Error "Database not found at $DbPath. Has the app been run at least once (to apply migrations and bootstrap)?"
    exit 1
}

Write-Host "DbPath:   $DbPath"

# Helper: run a SELECT and return the first column of the first row
# (or $null if no row). Uses sqlite3's -batch mode with -separator |
# to avoid quoting surprises.
function Invoke-Sqlite-Scalar([string]$sql) {
    $result = & sqlite3 -batch -separator '|' $DbPath $sql
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 query failed: $sql"
        exit 1
    }
    if ([string]::IsNullOrWhiteSpace($result)) { return $null }
    # Multi-row: take the first line. Multi-column: take the first
    # field. The two callers below only need scalar reads.
    return ($result -split "`n")[0].Trim().Split('|')[0]
}

# Helper: run a non-query (INSERT/UPDATE).
function Invoke-Sqlite-NonQuery([string]$sql) {
    & sqlite3 -batch $DbPath $sql
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 non-query failed: $sql"
        exit 1
    }
}

# --- Resolve target user -------------------------------------------

if ([string]::IsNullOrWhiteSpace($Username)) {
    # Default to the bootstrap administrator: the unique member of
    # the seeded "Administrator" role.
    Write-Host "Resolving target user via Administrator role membership..."
    $userIdSql = @"
SELECT u.Id
FROM Users u
JOIN UserRoles ur ON ur.UserId = u.Id
JOIN Roles r ON r.Id = ur.RoleId
WHERE r.Name = 'Administrator' AND u.IsDeleted = 0
LIMIT 1;
"@
    $UserId = Invoke-Sqlite-Scalar $userIdSql
    if (-not $UserId) {
        Write-Error "No Administrator user found. Has bootstrap completed?"
        exit 1
    }
    $usernameLookup = Invoke-Sqlite-Scalar "SELECT Username FROM Users WHERE Id = '$UserId';"
    Write-Host "Target user: $usernameLookup ($UserId)"
} else {
    $UserId = Invoke-Sqlite-Scalar "SELECT Id FROM Users WHERE Username = '$Username' AND IsDeleted = 0;"
    if (-not $UserId) {
        Write-Error "User '$Username' not found."
        exit 1
    }
    Write-Host "Target user: $Username ($UserId)"
}

# --- Grant the three C6a permissions -------------------------------

# Match EF Core's SQLite DateTime serialization format exactly:
# space separator, no timezone marker, 7 fractional digits. SQLite
# stores DateTime as TEXT and the resolver's filter
# (EffectiveFromUtc <= asOfUtc) is a lexicographic string
# comparison. EF Core's asOfUtc parameter is rendered using this
# format; any other format (e.g. ISO-8601 'T' separator with 'Z'
# suffix) sorts incorrectly and the row gets filtered out even
# though the underlying instant is earlier than asOfUtc. Sample
# EF-written row for reference: '2026-05-16 05:54:24.6049338'
# (UserRoles.EffectiveFromUtc from a real bootstrap).
$nowUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss.fffffff')
$actor  = 'smoke-setup'

# C6a + C6b smoke-author permission set. Smoke-setup scripts grow
# per-phase to cover each phase's permission-gated affordances:
# C6a added the three Draft-stage permissions (Create/EditDraft/
# HardDelete); C6b extends with the four review-flow permissions
# the smoke walk needs admin to hold as the author persona.
#
# Document.AssignReviewers note: per ADR 0008 §"Authorization",
# this permission is intentionally NOT assigned to the seeded
# QualityManager role (organizations grant it either to authors
# in the small-shop default or restrict it to QM in the
# strict-gatekeeper policy). The Administrator role doesn't grant
# it either (PermissionNames.All is system-only by design). So
# admin can ONLY acquire Document.AssignReviewers via a direct
# UserPermission row written by this script — there is no other
# path on a fresh-bootstrap install.
$permissionsToGrant = @(
    'Document.Create',
    'Document.EditDraft',
    'Document.HardDelete',
    'Document.SubmitForReview',
    'Document.AssignReviewers',
    'Document.ReturnForEdits',
    'Document.Review'
)

$grantedCount = 0
$skippedCount = 0

foreach ($permName in $permissionsToGrant) {
    $permId = Invoke-Sqlite-Scalar "SELECT Id FROM Permissions WHERE Name = '$permName';"
    if (-not $permId) {
        Write-Error "Permission '$permName' not found. Has the Phase 2 migration been applied?"
        exit 1
    }

    # Idempotency check: skip if an effective grant already exists
    # for (user, permission). 'Effective' here means EffectiveToUtc
    # is NULL (open-ended) AND IsDeleted = 0 — matches the production
    # resolver's filter. The EffectivePeriod owned type is flattened
    # into the UserPermissions table as plain EffectiveFromUtc /
    # EffectiveToUtc columns (no owned-type prefix) per the
    # AddPermissionsAndLinkTables migration.
    $existing = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserPermissions
WHERE UserId = '$UserId'
  AND PermissionId = '$permId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

    if ($existing) {
        Write-Host "  [skip]   $permName (already granted)"
        $skippedCount++
        continue
    }

    # Generate a fresh Guid for the new row in Microsoft.Data.Sqlite's
    # UPPERCASE TEXT canonical form (see script-bottom helper
    # New-RowId comment for the underlying rationale).
    $newId = [guid]::NewGuid().ToString().ToUpper()

    # RowVersion is BLOB(8) in the schema; sqlite3 accepts X'...' hex
    # literals for blobs. Use a random 8-byte value so the row's
    # optimistic-concurrency token differs from any other row.
    $rowVersionBytes = [byte[]]::new(8)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($rowVersionBytes)
    $rowVersionHex = ($rowVersionBytes | ForEach-Object { $_.ToString('x2') }) -join ''

    Invoke-Sqlite-NonQuery @"
INSERT INTO UserPermissions (
    Id, UserId, PermissionId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$newId', '$UserId', '$permId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$rowVersionHex', 0
);
"@
    Write-Host "  [grant]  $permName"
    $grantedCount++
}

Write-Host ""
Write-Host "Done. Granted: $grantedCount. Already in place: $skippedCount."

# --- C6b: grant the smoke author the QualityManager role ----------
#
# ADR 0009 corner case (surfaced by C6b stop 9 smoke walk): the
# role-prompter (SignatureRolePrompter) filters eligible roles by
# permission via IRoleResolutionService.GetEligibleRolesForPermission,
# which iterates the user's CURRENT-USER-ACCESSOR-supplied
# RolePermissions snapshot. Direct UserPermission grants don't
# appear in any role bucket and the prompter throws
# InvalidOperationException for "no role grants this permission"
# even when the user effectively holds it. This is the defensive
# throw documented in ADR 0009.
#
# Short-term mitigation: give the smoke author membership in the
# seeded QualityManager role, which grants most Document.* perms
# (everything except Document.AssignReviewers). The direct
# UserPermission grants above remain harmless redundancy (the
# effective-permission set is a union). Document.AssignReviewers
# stays as a direct grant — no seeded role holds it per ADR 0008.
#
# Long-term: an ADR 0009 amendment, deferred — see SCRATCHPAD
# "Microsoft.Data.Sqlite Guid-case convention" and the broader
# "Author-as-self-reviewer policy" entries for related work.

$qualityManagerRoleId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Roles
WHERE Name = 'QualityManager' AND IsDeleted = 0
LIMIT 1;
"@
if (-not $qualityManagerRoleId) {
    Write-Error "QualityManager role not found. Has the AddDocumentControllerTables migration applied?"
    exit 1
}

$existingAuthorUR = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserRoles
WHERE UserId = '$UserId'
  AND RoleId = '$qualityManagerRoleId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

if ($existingAuthorUR) {
    Write-Host "  [skip]   smoke author -> QualityManager UserRole already exists"
} else {
    $authorUrId = [guid]::NewGuid().ToString().ToUpper()
    $authorUrRvBytes = [byte[]]::new(8)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($authorUrRvBytes)
    $authorUrRvHex = ($authorUrRvBytes | ForEach-Object { $_.ToString('x2') }) -join ''
    Invoke-Sqlite-NonQuery @"
INSERT INTO UserRoles (
    Id, UserId, RoleId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$authorUrId', '$UserId', '$qualityManagerRoleId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$authorUrRvHex', 0
);
"@
    Write-Host "  [link]   smoke author -> QualityManager (role-path access to Document.* perms; routes through role-prompter cleanly)"
}

# --- C6b: multi-role test user (optional) --------------------------

if ($CreateMultiRoleUser) {
    Write-Host ""
    Write-Host "=== C6b: multi-role test user ==="

    # Helper: generate a fresh Guid in Microsoft.Data.Sqlite's
    # UPPERCASE TEXT canonical form. Per the provider's documented
    # convention (https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types),
    # GUID values are stored as TEXT using Guid.ToString().ToUpper().
    # Microsoft.Data.Sqlite UPPERCASES Guid parameter bindings at
    # runtime, and SQLite's `=` operator is case-sensitive on TEXT.
    # Script-inserted rows must match the case convention or runtime
    # EF queries (e.g., GetUsersWithPermissionAsync's IN-clause join
    # back to Users) silently return zero matches even though the
    # rows exist with the correct value. C6b stop 9 smoke walk
    # surfaced this when reviewer-picker queries returned only
    # admin (bootstrap-written, uppercase) and never the
    # script-written multireviewer/secondreviewer rows.
    #
    # PowerShell's `[guid]::NewGuid().ToString()` returns lowercase
    # ("D" format) by default — the .ToUpper() is essential.
    function New-RowId { return [guid]::NewGuid().ToString().ToUpper() }

    # Helper: generate a random 8-byte RowVersion blob. The schema
    # column is BLOB(8); sqlite3 accepts X'...' hex literals. Returning
    # the hex-without-prefix lets callers compose the literal inline.
    function New-RowVersionHex {
        $bytes = [byte[]]::new(8)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        return ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
    }

    # ---- Multireviewer user ---------------------------------------
    # Check for an existing user first. The script is fully idempotent
    # — re-running on a populated DB skips every existing row.
    $existingUserId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Users
WHERE Username = '$MultiRoleUsername' AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingUserId) {
        Write-Host "  [skip]   user '$MultiRoleUsername' already exists ($existingUserId)"
        $multiUserId = $existingUserId
    } else {
        # PBKDF2 hash matching the production PasswordHasher:
        # PBKDF2-HMAC-SHA256, 16-byte salt, 32-byte hash output,
        # base64-encoded for storage. Iteration count matches
        # PasswordPolicy.DefaultIterationCount (600_000) so the
        # auth service's verify path produces a Success result
        # (not SuccessRequiresRehash).
        if ($MultiRolePassword.Length -lt 12) {
            Write-Error "MultiRolePassword must be at least 12 characters (PasswordPolicy.DefaultMinimumLength)."
            exit 1
        }
        $saltBytes = [byte[]]::new(16)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($saltBytes)
        $hashBytes = [System.Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
            $MultiRolePassword,
            $saltBytes,
            600000,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            32)
        $hashB64 = [Convert]::ToBase64String($hashBytes)
        $saltB64 = [Convert]::ToBase64String($saltBytes)

        $multiUserId = New-RowId
        $userRv = New-RowVersionHex
        Invoke-Sqlite-NonQuery @"
INSERT INTO Users (
    Id, Username, DisplayName,
    PasswordHash, PasswordSalt, PasswordIterationCount,
    MustChangePassword, FailedLoginCount, LockedUntilUtc,
    LastLoginUtc, IsDisabled,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$multiUserId', '$MultiRoleUsername', 'Multi-role Reviewer (smoke)',
    '$hashB64', '$saltB64', 600000,
    0, 0, NULL,
    NULL, 0,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$userRv', 0
);
"@
        Write-Host "  [user]   created '$MultiRoleUsername' ($multiUserId) — password '$MultiRolePassword'"
    }

    # ---- multireviewer direct grant: Document.AssignReviewers ----
    #
    # Smoke pragmatism: the deployment model intent (per user
    # clarification at C6b stop 9, 2026-05-16) is that
    # author-can-submit is the small-shop default, but
    # `Document.AssignReviewers` is intentionally omitted from the
    # seeded QualityManager role per ADR 0008 and granted by
    # organizations either to authors (small-shop) or restricted
    # to QM (strict-gatekeeper). Neither default lands at seed
    # time, so on a fresh-bootstrap install no user holds
    # `AssignReviewers` until a script grants it.
    #
    # The C6b smoke walk needs multireviewer (operational author
    # persona) to submit Doc B for review. Without this direct
    # grant, the Submit-for-review affordance is correctly
    # hidden by `CanSubmitForReview`'s AND-gate. Granting it
    # here is the same "papering over a seed gap" pattern as the
    # admin-side grants in the default-mode block — see
    # SCRATCHPAD "Smoke-setup scripts grow per-phase" for the
    # full pattern + the architectural follow-up that should
    # reconcile the seed shape.
    $assignReviewersPermId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Permissions
WHERE Name = 'Document.AssignReviewers' AND IsDeleted = 0
LIMIT 1;
"@
    if (-not $assignReviewersPermId) {
        Write-Error "Document.AssignReviewers permission not found. Has the AddDocumentControllerTables migration applied?"
        exit 1
    }

    $existingAssignReviewersGrant = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserPermissions
WHERE UserId = '$multiUserId'
  AND PermissionId = '$assignReviewersPermId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingAssignReviewersGrant) {
        Write-Host "  [skip]   '$MultiRoleUsername' Document.AssignReviewers grant already exists"
    } else {
        $arGrantId = New-RowId
        $arRvBytes = [byte[]]::new(8)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($arRvBytes)
        $arRvHex = ($arRvBytes | ForEach-Object { $_.ToString('x2') }) -join ''
        Invoke-Sqlite-NonQuery @"
INSERT INTO UserPermissions (
    Id, UserId, PermissionId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$arGrantId', '$multiUserId', '$assignReviewersPermId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$arRvHex', 0
);
"@
        Write-Host "  [grant]  '$MultiRoleUsername' -> Document.AssignReviewers (direct UserPermission; smoke-only; see SCRATCHPAD)"
    }

    # ---- Secondary role -------------------------------------------
    $secondaryRoleId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Roles
WHERE Name = '$SecondaryRoleName' AND IsDeleted = 0
LIMIT 1;
"@

    if ($secondaryRoleId) {
        Write-Host "  [skip]   role '$SecondaryRoleName' already exists ($secondaryRoleId)"
    } else {
        $secondaryRoleId = New-RowId
        $roleRv = New-RowVersionHex
        Invoke-Sqlite-NonQuery @"
INSERT INTO Roles (
    Id, Name, Description,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$secondaryRoleId', '$SecondaryRoleName',
    'C6b smoke role granting only Document.Review. Combined with QualityManager on the multireviewer user so the SignAsRoleDialog sees two eligible roles for the Document.Review permission.',
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$roleRv', 0
);
"@
        Write-Host "  [role]   created '$SecondaryRoleName' ($secondaryRoleId)"
    }

    # ---- Resolve QualityManager role id ---------------------------
    $qualityManagerRoleId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Roles
WHERE Name = 'QualityManager' AND IsDeleted = 0
LIMIT 1;
"@
    if (-not $qualityManagerRoleId) {
        Write-Error "QualityManager role not found. Has the AddDocumentControllerTables migration applied?"
        exit 1
    }

    # ---- Secondary role -> Document.Review RolePermission ---------
    # Owned-type column flattening: EffectivePeriod owned type is
    # written as flat EffectiveFromUtc / EffectiveToUtc columns
    # (no owned-type prefix) per the AddPermissionsAndLinkTables
    # migration. The same DateTime serialization discipline as
    # the UserPermission grant block above applies — space
    # separator, 7 fractional digits, no T/Z markers.
    $documentReviewPermId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Permissions
WHERE Name = 'Document.Review' AND IsDeleted = 0
LIMIT 1;
"@
    if (-not $documentReviewPermId) {
        Write-Error "Document.Review permission not found. Has the AddDocumentControllerTables migration applied?"
        exit 1
    }

    $existingRoleLink = Invoke-Sqlite-Scalar @"
SELECT Id FROM RolePermissions
WHERE RoleId = '$secondaryRoleId'
  AND PermissionId = '$documentReviewPermId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingRoleLink) {
        Write-Host "  [skip]   '$SecondaryRoleName' -> Document.Review link already exists"
    } else {
        $rpId = New-RowId
        $rpRv = New-RowVersionHex
        Invoke-Sqlite-NonQuery @"
INSERT INTO RolePermissions (
    Id, RoleId, PermissionId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$rpId', '$secondaryRoleId', '$documentReviewPermId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$rpRv', 0
);
"@
        Write-Host "  [link]   '$SecondaryRoleName' -> Document.Review"
    }

    # ---- User -> roles (UserRole rows) ----------------------------
    # The multireviewer needs BOTH role memberships so the
    # role-prompter sees two eligible roles for Document.Review
    # (QualityManager grants it; ReviewerSecondary grants it
    # too). The two grants are inserted independently so re-runs
    # restore either side if one was manually deleted.
    foreach ($roleAssignment in @(
        @{ Name = 'QualityManager'; Id = $qualityManagerRoleId },
        @{ Name = $SecondaryRoleName; Id = $secondaryRoleId }
    )) {
        $existingUR = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserRoles
WHERE UserId = '$multiUserId'
  AND RoleId = '$($roleAssignment.Id)'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

        if ($existingUR) {
            Write-Host "  [skip]   '$MultiRoleUsername' -> $($roleAssignment.Name) UserRole already exists"
        } else {
            $urId = New-RowId
            $urRv = New-RowVersionHex
            Invoke-Sqlite-NonQuery @"
INSERT INTO UserRoles (
    Id, UserId, RoleId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$urId', '$multiUserId', '$($roleAssignment.Id)',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$urRv', 0
);
"@
            Write-Host "  [link]   '$MultiRoleUsername' -> $($roleAssignment.Name)"
        }
    }

    Write-Host ""
    Write-Host "C6b multi-role user ready. Sign in as '$MultiRoleUsername' with password '$MultiRolePassword' to exercise the SignAsRoleDialog path."

    # ---- secondreviewer single-role user (smoke walk Doc A) -------
    # Per the C6b stop-9 self-assignment reconciliation (SCRATCHPAD
    # "Author-as-self-reviewer policy"), the C3 service guard
    # forbids the author appearing in their own reviewer list. The
    # smoke walk needs Doc A to have TWO reviewers so the
    # not-last-signer path is exercised. secondreviewer is the
    # second reviewer alongside multireviewer; QualityManager-only
    # so they're single-role for Document.Review (auto-pick path).
    $existingSecondId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Users
WHERE Username = '$SecondReviewerUsername' AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingSecondId) {
        Write-Host "  [skip]   user '$SecondReviewerUsername' already exists ($existingSecondId)"
        $secondUserId = $existingSecondId
    } else {
        if ($SecondReviewerPassword.Length -lt 12) {
            Write-Error "SecondReviewerPassword must be at least 12 characters (PasswordPolicy.DefaultMinimumLength)."
            exit 1
        }
        $saltBytes2 = [byte[]]::new(16)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($saltBytes2)
        $hashBytes2 = [System.Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
            $SecondReviewerPassword,
            $saltBytes2,
            600000,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            32)
        $hashB642 = [Convert]::ToBase64String($hashBytes2)
        $saltB642 = [Convert]::ToBase64String($saltBytes2)

        $secondUserId = New-RowId
        $user2Rv = New-RowVersionHex
        Invoke-Sqlite-NonQuery @"
INSERT INTO Users (
    Id, Username, DisplayName,
    PasswordHash, PasswordSalt, PasswordIterationCount,
    MustChangePassword, FailedLoginCount, LockedUntilUtc,
    LastLoginUtc, IsDisabled,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$secondUserId', '$SecondReviewerUsername', 'Second Reviewer (smoke)',
    '$hashB642', '$saltB642', 600000,
    0, 0, NULL,
    NULL, 0,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$user2Rv', 0
);
"@
        Write-Host "  [user]   created '$SecondReviewerUsername' ($secondUserId) — password '$SecondReviewerPassword'"
    }

    # secondreviewer -> QualityManager only (single role for
    # Document.Review, so the SignAsRoleDialog auto-picks).
    $existingSecondUR = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserRoles
WHERE UserId = '$secondUserId'
  AND RoleId = '$qualityManagerRoleId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingSecondUR) {
        Write-Host "  [skip]   '$SecondReviewerUsername' -> QualityManager UserRole already exists"
    } else {
        $urId2 = New-RowId
        $ur2Rv = New-RowVersionHex
        Invoke-Sqlite-NonQuery @"
INSERT INTO UserRoles (
    Id, UserId, RoleId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$urId2', '$secondUserId', '$qualityManagerRoleId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$ur2Rv', 0
);
"@
        Write-Host "  [link]   '$SecondReviewerUsername' -> QualityManager"
    }

    Write-Host ""
    Write-Host "C6b second-reviewer user ready. Sign in as '$SecondReviewerUsername' with password '$SecondReviewerPassword' for the not-last-signer step."
}

Write-Host ""
Write-Host "Re-launch EasySynQ and sign in to see the Documents nav row with create/edit/delete affordances."

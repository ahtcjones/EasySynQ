# Smoke-setup helper for Phase 2 Document Controller.
#
# Per ADR 0011, the Phase 2 migration chain seeds two operational
# roles — `DocumentAuthor` (Create / EditDraft / HardDelete /
# SubmitForReview / AssignReviewers) for the small-shop author-can-
# submit default, and `QualityManager` (every Phase 2 document
# permission) for the review-side gestures. The smoke walk uses
# real users assigned to those roles via `UserRole` rows — no
# direct `UserPermission` grants needed. Admin is the IT-side seat
# only; it administers users and roles but does not perform
# operational gestures.
#
# What this script does:
#   - Mints a `smokeauthor` user assigned to `DocumentAuthor`. The
#     C6a author-side smoke walk (sign in -> create document ->
#     upload PDF -> edit metadata -> replace PDF -> hard-delete)
#     runs as this user.
#   - With `-CreateMultiRoleUser`, additionally mints `multireviewer`
#     (in `QualityManager` + a new `ReviewerSecondary` role granting
#     `Document.Review`) and `secondreviewer` (in `QualityManager`
#     only). The C6b smoke walk uses these as the two reviewers on
#     `smokeauthor`'s submitted documents — multireviewer exercises
#     the multi-role `SignAsRoleDialog` path; secondreviewer
#     exercises the single-role auto-pick path. Either also handles
#     `Document.ReturnForEdits` / `Document.Retire` gestures since
#     `QualityManager` holds those permissions.
#   - Prints each minted user's effective permission set as a
#     pre-flight verification helper. The smoke walker visually
#     confirms before starting that each user holds the expected
#     permissions.
#
# What this script no longer does (post-ADR-0011):
#   - No direct `UserPermission` grants to admin. Admin is IT-side
#     only; ADR 0011's seeded roles cover every operational gesture.
#   - No `UserRole` row linking admin to `QualityManager`. Admin is
#     not a member of any operational role.
#   - No direct `UserPermission` grant of `Document.AssignReviewers`
#     to multireviewer. `QualityManager` now holds it via the
#     `AddDocumentAuthorRoleAndAmendQualityManagerSeed` migration's
#     amendment.
#
# Canonical use:
#   pwsh -File scripts/grant-document-permissions.ps1
#   pwsh -File scripts/grant-document-permissions.ps1 -CreateMultiRoleUser
#
# Optional parameters:
#   -DbPath              Override the SQLite path. Defaults to the
#                        production location:
#                        %LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db
#   -AuthorUsername      Username for the DocumentAuthor smoke user.
#                        Default: 'smokeauthor'.
#   -AuthorPassword      Plaintext password for the DocumentAuthor
#                        smoke user. Default: 'smokeauthor1234'
#                        (meets the 12-character minimum from
#                        PasswordPolicy).
#   -CreateMultiRoleUser Switch — also seed the C6b multi-role
#                        review users (multireviewer + secondreviewer
#                        + ReviewerSecondary role).
#   -MultiRoleUsername   Username for the C6b multi-role reviewer.
#                        Default: 'multireviewer'.
#   -MultiRolePassword   Plaintext password for the C6b multi-role
#                        reviewer. Default: 'multireviewer123'.
#   -SecondaryRoleName   Name for the secondary role granting only
#                        Document.Review. Default: 'ReviewerSecondary'.
#   -SecondReviewerUsername  Username for the C6b single-role
#                            reviewer. Default: 'secondreviewer'.
#   -SecondReviewerPassword  Plaintext password for the C6b single-
#                            role reviewer. Default: 'secondreviewer1'.
#
# Idempotent: re-running on a populated DB skips every existing row.
#
# Prerequisites:
#   sqlite3.exe on PATH. Download from https://www.sqlite.org/download.html
#   (the precompiled "sqlite-tools-win-x64-*.zip" bundle), unzip, and
#   put sqlite3.exe somewhere on PATH. No other tooling required.
#
# IMPORTANT: This script bypasses the standard-fields interceptor and
# the audit-log interceptor (it writes via raw sqlite3 INSERT). That is
# intentional for smoke setup — these are dev-only user / role grants
# that don't need to appear in the production audit trail. Production
# user / role provisioning goes through the admin UI (when it ships)
# which routes through the normal repository pipeline.

[CmdletBinding()]
param(
    [string]$DbPath = (Join-Path $env:LOCALAPPDATA 'EasySynQ\db\EasySynQ_Master.db'),
    [string]$AuthorUsername = 'smokeauthor',
    [string]$AuthorPassword = 'smokeauthor1234',
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

# Helper: run a SELECT and return all rows as an array of strings
# (one string per row, pipe-separated columns inline). Used by the
# effective-permission verification helper to enumerate permission
# names.
function Invoke-Sqlite-Lines([string]$sql) {
    $result = & sqlite3 -batch -separator '|' $DbPath $sql
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 query failed: $sql"
        exit 1
    }
    if ([string]::IsNullOrWhiteSpace($result)) { return @() }
    return $result -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

# Helper: run a non-query (INSERT/UPDATE).
function Invoke-Sqlite-NonQuery([string]$sql) {
    & sqlite3 -batch $DbPath $sql
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 non-query failed: $sql"
        exit 1
    }
}

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
# surfaced this when reviewer-picker queries returned only admin
# (bootstrap-written, uppercase) and never the script-written
# multireviewer/secondreviewer rows.
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

# Helper: mint a new User row with a PBKDF2-HMAC-SHA256 hash matching
# the production PasswordHasher (16-byte salt, 32-byte hash output,
# base64-encoded for storage, 600,000 iterations matching
# PasswordPolicy.DefaultIterationCount). Returns the new user's Id.
function New-User([string]$username, [string]$displayName, [string]$password) {
    if ($password.Length -lt 12) {
        Write-Error "Password for '$username' must be at least 12 characters (PasswordPolicy.DefaultMinimumLength)."
        exit 1
    }
    $saltBytes = [byte[]]::new(16)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($saltBytes)
    $hashBytes = [System.Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
        $password,
        $saltBytes,
        600000,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        32)
    $hashB64 = [Convert]::ToBase64String($hashBytes)
    $saltB64 = [Convert]::ToBase64String($saltBytes)

    $userId = New-RowId
    $userRv = New-RowVersionHex
    Invoke-Sqlite-NonQuery @"
INSERT INTO Users (
    Id, Username, DisplayName,
    PasswordHash, PasswordSalt, PasswordIterationCount,
    MustChangePassword, FailedLoginCount, LockedUntilUtc,
    LastLoginUtc, IsDisabled,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$userId', '$username', '$displayName',
    '$hashB64', '$saltB64', 600000,
    0, 0, NULL,
    NULL, 0,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$userRv', 0
);
"@
    return $userId
}

# Helper: write a UserRole row linking $userId to $roleId if no
# open-ended row already exists for that pair. Idempotent.
function New-UserRole([string]$userId, [string]$roleId, [string]$userLabel, [string]$roleLabel) {
    $existing = Invoke-Sqlite-Scalar @"
SELECT Id FROM UserRoles
WHERE UserId = '$userId'
  AND RoleId = '$roleId'
  AND EffectiveToUtc IS NULL
  AND IsDeleted = 0
LIMIT 1;
"@
    if ($existing) {
        Write-Host "  [skip]   '$userLabel' -> $roleLabel UserRole already exists"
        return
    }
    $urId = New-RowId
    $urRv = New-RowVersionHex
    Invoke-Sqlite-NonQuery @"
INSERT INTO UserRoles (
    Id, UserId, RoleId,
    EffectiveFromUtc, EffectiveToUtc,
    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc, RowVersion, IsDeleted
) VALUES (
    '$urId', '$userId', '$roleId',
    '$nowUtc', NULL,
    '$actor', '$nowUtc', '$actor', '$nowUtc', X'$urRv', 0
);
"@
    Write-Host "  [link]   '$userLabel' -> $roleLabel"
}

# Helper: print the effective permission set for a user, computed
# as the union of role-derived permissions (UserRole -> Role ->
# RolePermission -> Permission, filtered to currently-effective
# rows) and direct UserPermission grants (also currently-effective).
# Matches the resolver's filter shape on EffectiveToUtc IS NULL +
# IsDeleted = 0. Pre-flight verification — the smoke walker
# visually confirms each user holds the expected permissions
# before starting the walk.
function Show-EffectivePermissions([string]$userId, [string]$label) {
    Write-Host ""
    Write-Host "  Effective permissions for '$label':"
    $names = Invoke-Sqlite-Lines @"
SELECT DISTINCT p.Name FROM Permissions p
JOIN RolePermissions rp ON rp.PermissionId = p.Id
JOIN UserRoles ur ON ur.RoleId = rp.RoleId
WHERE ur.UserId = '$userId'
  AND ur.EffectiveToUtc IS NULL AND ur.IsDeleted = 0
  AND rp.EffectiveToUtc IS NULL AND rp.IsDeleted = 0
  AND p.IsDeleted = 0
UNION
SELECT DISTINCT p.Name FROM Permissions p
JOIN UserPermissions up ON up.PermissionId = p.Id
WHERE up.UserId = '$userId'
  AND up.EffectiveToUtc IS NULL AND up.IsDeleted = 0
  AND p.IsDeleted = 0
ORDER BY 1;
"@
    if (-not $names -or $names.Count -eq 0) {
        Write-Host "    (no effective permissions — the smoke walk will fail authorization checks)"
        return
    }
    foreach ($name in $names) {
        Write-Host "    - $name"
    }
}

# --- Resolve seeded role Ids ---------------------------------------

$documentAuthorRoleId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Roles
WHERE Name = 'DocumentAuthor' AND IsDeleted = 0
LIMIT 1;
"@
if (-not $documentAuthorRoleId) {
    Write-Error "DocumentAuthor role not found. Has the AddDocumentAuthorRoleAndAmendQualityManagerSeed migration applied?"
    exit 1
}

$qualityManagerRoleId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Roles
WHERE Name = 'QualityManager' AND IsDeleted = 0
LIMIT 1;
"@
if (-not $qualityManagerRoleId) {
    Write-Error "QualityManager role not found. Has the AddDocumentControllerTables migration applied?"
    exit 1
}

# --- Mint the DocumentAuthor smoke user ----------------------------

Write-Host ""
Write-Host "=== DocumentAuthor smoke user ==="

$existingAuthorId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Users
WHERE Username = '$AuthorUsername' AND IsDeleted = 0
LIMIT 1;
"@

if ($existingAuthorId) {
    Write-Host "  [skip]   user '$AuthorUsername' already exists ($existingAuthorId)"
    $authorUserId = $existingAuthorId
} else {
    $authorUserId = New-User -username $AuthorUsername -displayName 'Smoke Author' -password $AuthorPassword
    Write-Host "  [user]   created '$AuthorUsername' ($authorUserId) — password '$AuthorPassword'"
}

# DocumentAuthor membership — gives the smoke user every author-side
# permission needed by the C6a walk (Create / EditDraft / HardDelete
# / SubmitForReview / AssignReviewers) via the seeded role.
New-UserRole -userId $authorUserId -roleId $documentAuthorRoleId `
    -userLabel $AuthorUsername -roleLabel 'DocumentAuthor'

Show-EffectivePermissions -userId $authorUserId -label $AuthorUsername

# --- C6b: multi-role test users (optional) -------------------------

if ($CreateMultiRoleUser) {
    Write-Host ""
    Write-Host "=== C6b: review users ==="

    # ---- Multireviewer user ---------------------------------------
    $existingMultiId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Users
WHERE Username = '$MultiRoleUsername' AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingMultiId) {
        Write-Host "  [skip]   user '$MultiRoleUsername' already exists ($existingMultiId)"
        $multiUserId = $existingMultiId
    } else {
        $multiUserId = New-User -username $MultiRoleUsername -displayName 'Multi-role Reviewer (smoke)' -password $MultiRolePassword
        Write-Host "  [user]   created '$MultiRoleUsername' ($multiUserId) — password '$MultiRolePassword'"
    }

    # ---- Secondary role (single-permission role granting Document.Review)
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

    # ---- Secondary role -> Document.Review RolePermission ---------
    # Owned-type column flattening: EffectivePeriod owned type is
    # written as flat EffectiveFromUtc / EffectiveToUtc columns
    # (no owned-type prefix) per the AddPermissionsAndLinkTables
    # migration. The same DateTime serialization discipline as
    # everywhere else in this script applies — space separator, 7
    # fractional digits, no T/Z markers.
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

    # multireviewer needs BOTH QualityManager and ReviewerSecondary
    # so the role-prompter sees two eligible roles for
    # Document.Review (QualityManager grants it; ReviewerSecondary
    # grants it too). No direct UserPermission grants needed —
    # QualityManager now holds AssignReviewers (per ADR 0011's
    # amendment), so multireviewer can also exercise submit-side
    # gestures from their QM membership if a test scenario requires
    # it.
    New-UserRole -userId $multiUserId -roleId $qualityManagerRoleId `
        -userLabel $MultiRoleUsername -roleLabel 'QualityManager'
    New-UserRole -userId $multiUserId -roleId $secondaryRoleId `
        -userLabel $MultiRoleUsername -roleLabel $SecondaryRoleName

    Show-EffectivePermissions -userId $multiUserId -label $MultiRoleUsername

    # ---- secondreviewer single-role user --------------------------
    # QualityManager-only so SignAsRoleDialog auto-picks (single
    # eligible role for Document.Review). Pairs with multireviewer
    # for the two-reviewer not-last-signer walk.
    $existingSecondId = Invoke-Sqlite-Scalar @"
SELECT Id FROM Users
WHERE Username = '$SecondReviewerUsername' AND IsDeleted = 0
LIMIT 1;
"@

    if ($existingSecondId) {
        Write-Host "  [skip]   user '$SecondReviewerUsername' already exists ($existingSecondId)"
        $secondUserId = $existingSecondId
    } else {
        $secondUserId = New-User -username $SecondReviewerUsername -displayName 'Second Reviewer (smoke)' -password $SecondReviewerPassword
        Write-Host "  [user]   created '$SecondReviewerUsername' ($secondUserId) — password '$SecondReviewerPassword'"
    }

    New-UserRole -userId $secondUserId -roleId $qualityManagerRoleId `
        -userLabel $SecondReviewerUsername -roleLabel 'QualityManager'

    Show-EffectivePermissions -userId $secondUserId -label $SecondReviewerUsername
}

Write-Host ""
Write-Host "Smoke users ready. Re-launch EasySynQ and sign in as:"
Write-Host "  - '$AuthorUsername' / '$AuthorPassword' (DocumentAuthor) for author-side gestures (Create / EditDraft / HardDelete / SubmitForReview / AssignReviewers)."
if ($CreateMultiRoleUser) {
    Write-Host "  - '$MultiRoleUsername' / '$MultiRolePassword' (QualityManager + $SecondaryRoleName) for review gestures including the multi-role SignAsRoleDialog path."
    Write-Host "  - '$SecondReviewerUsername' / '$SecondReviewerPassword' (QualityManager) for the single-role auto-pick review path and ReturnForEdits / Retire gestures."
}

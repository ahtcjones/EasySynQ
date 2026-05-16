# Smoke-setup helper for Phase 2 C6a.
#
# Grants the three C6a author permissions (Document.Create,
# Document.EditDraft, Document.HardDelete) to a target user by
# inserting UserPermission rows directly into the SQLite database.
# Used to prepare a fresh bootstrap install so the smoke walkthrough
# (sign in -> create document -> upload PDF -> edit metadata ->
# replace PDF -> hard-delete) has the permissions it needs.
#
# Canonical use:
#   pwsh -File scripts/grant-document-permissions.ps1
#
# Optional parameters:
#   -DbPath   Override the SQLite path. Defaults to the production
#             location: %LOCALAPPDATA%\EasySynQ\db\EasySynQ_Master.db
#   -Username Override the target username. Defaults to the bootstrap
#             user (the unique member of the seeded Administrator role).
#
# Idempotent: re-running on a user who already holds the permissions
# is a no-op. Existing rows are detected by (UserId, PermissionId)
# match and skipped.
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
    [string]$Username = $null
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
$permissionsToGrant = @(
    'Document.Create',
    'Document.EditDraft',
    'Document.HardDelete'
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

    # Generate a fresh Guid for the new row. PowerShell's [guid]::NewGuid()
    # returns the lower-case dashed form EF Core uses by default.
    $newId = [guid]::NewGuid().ToString()

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
Write-Host ""
Write-Host "Re-launch EasySynQ and sign in to see the Documents nav row with create/edit/delete affordances."

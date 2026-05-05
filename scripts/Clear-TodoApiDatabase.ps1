param(
    [string]$DatabaseTarget,
    [string]$ConnectionString,
    [switch]$IncludeHangfire
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appSettingsPath = Join-Path $repoRoot "TodoApi\appsettings.json"

if (-not $ConnectionString) {
    if (-not (Test-Path -LiteralPath $appSettingsPath)) {
        throw "Could not find TodoApi appsettings.json at '$appSettingsPath'."
    }

    $settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json

    if (-not $DatabaseTarget) {
        $DatabaseTarget = if ($env:DatabaseTarget) { $env:DatabaseTarget } else { $settings.DatabaseTarget }
    }

    $envConnectionStringName = "ConnectionStrings__$DatabaseTarget"
    $ConnectionString = [Environment]::GetEnvironmentVariable($envConnectionStringName)

    if (-not $ConnectionString) {
        $ConnectionString = $settings.ConnectionStrings.$DatabaseTarget
    }
}

if (-not $ConnectionString) {
    throw "No connection string found. Pass -ConnectionString or set -DatabaseTarget to a configured target."
}

$sql = @"
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[SyncEvents]', N'U') IS NOT NULL
    DELETE FROM [dbo].[SyncEvents];

IF OBJECT_ID(N'[dbo].[Items]', N'U') IS NOT NULL
    DELETE FROM [dbo].[Items];

IF OBJECT_ID(N'[dbo].[TodoList]', N'U') IS NOT NULL
    DELETE FROM [dbo].[TodoList];

IF OBJECT_ID(N'[dbo].[SyncEvents]', N'U') IS NOT NULL
    DBCC CHECKIDENT ('[dbo].[SyncEvents]', RESEED, 0) WITH NO_INFOMSGS;

IF OBJECT_ID(N'[dbo].[Items]', N'U') IS NOT NULL
    DBCC CHECKIDENT ('[dbo].[Items]', RESEED, 0) WITH NO_INFOMSGS;

IF OBJECT_ID(N'[dbo].[TodoList]', N'U') IS NOT NULL
    DBCC CHECKIDENT ('[dbo].[TodoList]', RESEED, 0) WITH NO_INFOMSGS;

COMMIT TRANSACTION;
"@

if ($IncludeHangfire) {
    $sql += @"

IF SCHEMA_ID(N'HangFire') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[HangFire].[State]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[State];
    IF OBJECT_ID(N'[HangFire].[JobParameter]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[JobParameter];
    IF OBJECT_ID(N'[HangFire].[JobQueue]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[JobQueue];
    IF OBJECT_ID(N'[HangFire].[Job]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[Job];
    IF OBJECT_ID(N'[HangFire].[Counter]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[Counter];
    IF OBJECT_ID(N'[HangFire].[AggregatedCounter]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[AggregatedCounter];
    IF OBJECT_ID(N'[HangFire].[List]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[List];
    IF OBJECT_ID(N'[HangFire].[Set]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[Set];
    IF OBJECT_ID(N'[HangFire].[Hash]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[Hash];
    IF OBJECT_ID(N'[HangFire].[Server]', N'U') IS NOT NULL
        DELETE FROM [HangFire].[Server];
END;
"@
}

Add-Type -AssemblyName System.Data

$connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$command = $connection.CreateCommand()
$command.CommandText = $sql
$command.CommandTimeout = 60

try {
    $connection.Open()
    [void]$command.ExecuteNonQuery()
    Write-Host "TodoApi database data cleared successfully."
}
finally {
    $connection.Dispose()
}

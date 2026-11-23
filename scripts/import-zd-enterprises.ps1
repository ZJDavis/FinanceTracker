<#
.SYNOPSIS
    Adds the synthetic ZD Enterprises ledger to a FinanceTracker SQLite database.

.DESCRIPTION
    The import is additive and idempotent. It defaults to the current user's
    FinanceTracker database and can target another SQLite file with -DatabasePath.
#>
[CmdletBinding()]
param(
    [string] $DatabasePath = (Join-Path $env:LOCALAPPDATA 'FinanceTracker\finance.db'),
    [string] $DatasetPath = (Join-Path $PSScriptRoot '..\data\demo\zd-enterprises-transactions.json')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$importerProject = Join-Path $repoRoot 'Tools\FinanceTracker.DemoDataImporter\FinanceTracker.DemoDataImporter.csproj'
$resolvedDatasetPath = (Resolve-Path -LiteralPath $DatasetPath).Path
$resolvedDatabasePath = [System.IO.Path]::GetFullPath($DatabasePath)

Write-Host "Importing ZD Enterprises synthetic transactions into $resolvedDatabasePath"
& dotnet run --project $importerProject --configuration Release -- $resolvedDatasetPath $resolvedDatabasePath
if ($LASTEXITCODE -ne 0) {
    throw "The ZD Enterprises import failed with exit code $LASTEXITCODE."
}

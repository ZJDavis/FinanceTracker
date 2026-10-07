$ErrorActionPreference = 'Stop'
$localToolsPath = Join-Path (Split-Path $PSScriptRoot -Parent) '.finance-tools'
$localDotnetPath = Join-Path $localToolsPath 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnetPath) {
    $dotnetCommand = $localDotnetPath
    $env:DOTNET_CLI_HOME = Join-Path $localToolsPath 'home'
    $env:NUGET_PACKAGES = Join-Path $localToolsPath 'packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
} else {
    $dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
}
& $dotnetCommand build (Join-Path $PSScriptRoot 'FinanceTracker.sln') --nologo -m:1
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
& $dotnetCommand test (Join-Path $PSScriptRoot 'Tests\Unit\FinanceTracker.UnitTests.csproj') --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
& $dotnetCommand run --project (Join-Path $PSScriptRoot 'Tests\FinanceTracker.Regression.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }

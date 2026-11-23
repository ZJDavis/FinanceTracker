<#
.SYNOPSIS
    Starts, checks, inspects, or stops the local FinanceTracker backend stack.

.DESCRIPTION
    Wraps Docker Compose for the FinanceTracker API and PostgreSQL demo stack.
    The API is expected to publish its container port 8080 on loopback using
    FINANCETRACKER_API_PORT (default 5080), with /health/ready as its readiness
    endpoint. A local .env file is created on first use with a random password.

.PARAMETER Action
    up: build and start the stack, then wait for API readiness.
    check: check Compose state and the API readiness endpoint.
    status: show Compose state and readiness when available.
    logs: stream Compose logs.
    down: stop and remove containers while preserving the database volume.

.PARAMETER ApiPort
    Local loopback port used to reach the API. Defaults to 5080.

.EXAMPLE
    .\scripts\backend.ps1 up

.EXAMPLE
    .\scripts\backend.ps1 up -ApiPort 5081

.EXAMPLE
    .\scripts\backend.ps1 down
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'check', 'status', 'logs', 'down')]
    [string] $Action = 'up',

    [ValidateRange(1, 65535)]
    [int] $ApiPort = 5080,

    [ValidateRange(1, 600)]
    [int] $ReadyTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.yml'
$environmentFile = Join-Path $repoRoot '.env'
$readyUrl = "http://127.0.0.1:$ApiPort/health/ready"
$env:FINANCETRACKER_API_PORT = [string] $ApiPort

function Ensure-LocalEnvironmentFile {
    if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
        [System.IO.File]::WriteAllText($environmentFile, "# Generated locally by scripts/backend.ps1`r`n", [System.Text.UTF8Encoding]::new($false))
    }

    $environmentLines = @(Get-Content -LiteralPath $environmentFile)
    $hasDatabasePassword = $environmentLines | Where-Object { $_ -match '^FINANCETRACKER_POSTGRES_PASSWORD=.+$' }
    if (-not $hasDatabasePassword) {
        $generatedPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
        Add-Content -LiteralPath $environmentFile -Value "FINANCETRACKER_POSTGRES_PASSWORD=$generatedPassword" -Encoding utf8
        Write-Host 'Created a local .env file with a random PostgreSQL password.'
    }
}

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]] $Arguments)

    Push-Location $repoRoot
    try {
        & docker compose --file $composeFile @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Test-ApiReady {
    try {
        $response = Invoke-RestMethod -Uri $readyUrl -Method Get -TimeoutSec 3
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForApiReady {
    $deadline = [DateTime]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
    Write-Host "Waiting for API readiness at $readyUrl ..."

    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-ApiReady) {
            Write-Host 'FinanceTracker API is ready.' -ForegroundColor Green
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "The API did not become ready within $ReadyTimeoutSeconds seconds. Inspect it with: .\scripts\backend.ps1 logs"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI was not found. Install Docker Desktop/Engine and ensure it is on PATH.'
}

Ensure-LocalEnvironmentFile

if (-not (Test-Path -LiteralPath $composeFile -PathType Leaf)) {
    throw "Docker Compose file not found: $composeFile. It is added with the backend implementation."
}

switch ($Action) {
    'up' {
        & docker info *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker is installed but its engine is not available. Start Docker Desktop/Engine and retry.'
        }

        Invoke-Compose -Arguments @('config', '--quiet')
        Invoke-Compose -Arguments @('up', '--build', '--detach')
        Wait-ForApiReady
        Write-Host "API: $readyUrl"
        Write-Host "Swagger: http://127.0.0.1:$ApiPort/swagger"
    }
    'check' {
        Invoke-Compose -Arguments @('ps')
        if (-not (Test-ApiReady)) {
            throw "FinanceTracker API is not ready at $readyUrl. Start it with: .\scripts\backend.ps1 up"
        }
        Write-Host 'FinanceTracker API is ready.' -ForegroundColor Green
    }
    'status' {
        Invoke-Compose -Arguments @('ps')
        if (Test-ApiReady) {
            Write-Host "API ready: $readyUrl" -ForegroundColor Green
        }
        else {
            Write-Host "API not ready: $readyUrl" -ForegroundColor Yellow
        }
    }
    'logs' {
        Invoke-Compose -Arguments @('logs', '--follow')
    }
    'down' {
        Invoke-Compose -Arguments @('down')
        Write-Host 'Backend containers stopped. The PostgreSQL demo volume was preserved.'
    }
}

param(
    [string]$ApiBaseUrl,
    [string]$FrontendBaseUrl,
    [string]$BusinessSlug = "demo-salon"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "1) Backend build (Release)..." -ForegroundColor Cyan
dotnet build "$repoRoot/MojTermin.Api.sln" -c Release

Write-Host "2) Full test suite..." -ForegroundColor Cyan
dotnet test "$repoRoot/MojTermin.Api.sln" -c Release

Write-Host "3) Frontend production build..." -ForegroundColor Cyan
Push-Location "$repoRoot/mojtermin-web"
try {
    npm run build
}
finally {
    Pop-Location
}

if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl) -and -not [string]::IsNullOrWhiteSpace($FrontendBaseUrl)) {
    Write-Host "4) Running smoke test..." -ForegroundColor Cyan
    & "$PSScriptRoot/smoke-test.ps1" `
        -ApiBaseUrl $ApiBaseUrl `
        -FrontendBaseUrl $FrontendBaseUrl `
        -BusinessSlug $BusinessSlug
}
else {
    Write-Host "4) Smoke test skipped (ApiBaseUrl/FrontendBaseUrl not provided)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Pre-release check passed." -ForegroundColor Green

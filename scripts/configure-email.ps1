# Configures MojTermin.Api email via dotnet user-secrets (local dev) or prints .env lines (Docker).
# Run from repo root:  .\scripts\configure-email.ps1
param(
    [ValidateSet("Resend", "Gmail", "Custom")]
    [string]$Provider = "Resend",
    [string]$ApiProject = "MojTermin.Api\MojTermin.Api.csproj",
    [switch]$DockerEnvOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot $ApiProject
if (-not (Test-Path $projectPath)) {
    throw "API project not found: $projectPath"
}

function Read-SecretValue {
    param([string]$Prompt, [switch]$AsSecure)
    if ($AsSecure) {
        $secure = Read-Host $Prompt -AsSecureString
        return [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    }
    return Read-Host $Prompt
}

Write-Host ""
Write-Host "MojTermin — email (SMTP) setup" -ForegroundColor Cyan
Write-Host ""

$senderName = "MojTermin"
$enabled = "true"
$useSsl = "true"
$port = "587"

switch ($Provider) {
    "Resend" {
        Write-Host "Resend (preporučeno za noreply@mojtermin.com):" -ForegroundColor Yellow
        Write-Host "  1. https://resend.com → Domains → dodaj mojtermin.com"
        Write-Host "  2. U DNS-u dodaj TXT/CNAME zapise koje Resend prikaže (SPF + DKIM)"
        Write-Host "  3. API Keys → Create → kopiraj ključ (re_...)"
        Write-Host ""
        $senderEmail = Read-SecretValue "Sender email (npr. noreply@mojtermin.com)"
        $smtpHost = "smtp.resend.com"
        $smtpUser = "resend"
        $smtpPass = Read-SecretValue "Resend API key (re_...)" -AsSecure
    }
    "Gmail" {
        Write-Host "Gmail (privremeno — From MORA biti ista Gmail adresa):" -ForegroundColor Yellow
        Write-Host "  Google Account → Security → 2FA → App passwords"
        Write-Host ""
        $gmail = Read-SecretValue "Gmail adresa (npr. tvoj@gmail.com)"
        $senderEmail = $gmail
        $smtpHost = "smtp.gmail.com"
        $smtpUser = $gmail
        $smtpPass = Read-SecretValue "Gmail app password (16 znakova)" -AsSecure
    }
    "Custom" {
        $senderEmail = Read-SecretValue "Sender email"
        $smtpHost = Read-SecretValue "SMTP host"
        $port = Read-SecretValue "SMTP port (587)"
        $smtpUser = Read-SecretValue "SMTP username"
        $smtpPass = Read-SecretValue "SMTP password" -AsSecure
    }
}

$envBlock = @"
NOTIFICATIONS_ENABLED=true
SENDER_EMAIL=$senderEmail
SMTP_HOST=$smtpHost
SMTP_PORT=$port
SMTP_USERNAME=$smtpUser
SMTP_PASSWORD=<paste-secret-here>
"@

if ($DockerEnvOnly) {
    Write-Host ""
    Write-Host "Dodaj u .env (SMTP_PASSWORD ručno):" -ForegroundColor Green
    Write-Host $envBlock
    exit 0
}

Push-Location (Split-Path $projectPath -Parent)
try {
    dotnet user-secrets set "Notifications:Enabled" $enabled --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SenderName" $senderName --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SenderEmail" $senderEmail --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SmtpHost" $smtpHost --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SmtpPort" $port --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:UseSsl" $useSsl --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SmtpUsername" $smtpUser --project (Split-Path $projectPath -Leaf)
    dotnet user-secrets set "Notifications:SmtpPassword" $smtpPass --project (Split-Path $projectPath -Leaf)
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "User-secrets spremljeni. Restartuj API, zatim test:" -ForegroundColor Green
Write-Host "  .\scripts\test-smtp.ps1 -To tvoj@email.com"
Write-Host ""

# Sends one test email using MojTermin.Api user-secrets SMTP settings.
# Usage: .\scripts\test-smtp.ps1 -To you@example.com
param(
    [Parameter(Mandatory = $true)]
    [string]$To,
    [string]$ApiProject = "MojTermin.Api\MojTermin.Api.csproj"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot $ApiProject
$projectDir = Split-Path $projectPath -Parent
$projectFile = Split-Path $projectPath -Leaf

function Get-Secret {
    param([string]$Key)
    $lines = dotnet user-secrets list --project $projectFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "dotnet user-secrets failed: $lines" }
    $prefix = "$Key = "
    $line = $lines | Where-Object { $_ -like "$prefix*" } | Select-Object -First 1
    if (-not $line) { throw "Missing user-secret: $Key" }
    return $line.Substring($prefix.Length).Trim()
}

Push-Location $projectDir
try {
    $enabled = Get-Secret "Notifications:Enabled"
    if ($enabled -ne "true") { throw "Notifications:Enabled is not true in user-secrets." }

    $fromEmail = Get-Secret "Notifications:SenderEmail"
    $fromName = Get-Secret "Notifications:SenderName"
    $smtpHost = Get-Secret "Notifications:SmtpHost"
    $port = [int](Get-Secret "Notifications:SmtpPort")
    $user = Get-Secret "Notifications:SmtpUsername"
    $pass = Get-Secret "Notifications:SmtpPassword"
    $ssl = (Get-Secret "Notifications:UseSsl") -eq "true"
}
finally {
    Pop-Location
}

$message = [System.Net.Mail.MailMessage]::new()
$message.From = [System.Net.Mail.MailAddress]::new($fromEmail, $fromName)
[void]$message.To.Add($To)
$message.Subject = "MojTermin SMTP test"
$message.Body = @"
Ovo je test poruka iz scripts/test-smtp.ps1.

Ako stigne u inbox (ne spam), SMTP je ispravno podešen.
Sljedeći korak: registracija, rezervacija, resend verification na API-ju.
"@

$client = [System.Net.Mail.SmtpClient]::new($smtpHost, $port)
$client.EnableSsl = $ssl
$client.Credentials = [System.Net.NetworkCredential]::new($user, $pass)

try {
    $client.Send($message)
    Write-Host "Test email poslan na $To (From: $fromEmail via $smtpHost)." -ForegroundColor Green
}
catch {
    Write-Host "Slanje nije uspjelo: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    $message.Dispose()
    $client.Dispose()
}

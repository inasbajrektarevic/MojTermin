param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$FrontendBaseUrl,

    [string]$BusinessSlug = "demo-salon"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Http200 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    Write-Host "Checking $Url ..."
    $response = Invoke-WebRequest -Uri $Url -Method Get
    if ($response.StatusCode -ne 200) {
        throw "Expected 200 from $Url, got $($response.StatusCode)."
    }
}

if ($ApiBaseUrl.EndsWith("/")) {
    $ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")
}

if ($FrontendBaseUrl.EndsWith("/")) {
    $FrontendBaseUrl = $FrontendBaseUrl.TrimEnd("/")
}

Assert-Http200 -Url "$ApiBaseUrl/health"
Assert-Http200 -Url "$ApiBaseUrl/api/businesses/by-slug/$BusinessSlug"
Assert-Http200 -Url "$ApiBaseUrl/api/services/public/$BusinessSlug"
Assert-Http200 -Url "$ApiBaseUrl/api/working-hours/public/$BusinessSlug"
Assert-Http200 -Url "$FrontendBaseUrl/b/$BusinessSlug"

Write-Host ""
Write-Host "Smoke test passed for API + frontend." -ForegroundColor Green

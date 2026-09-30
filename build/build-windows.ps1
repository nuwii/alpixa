<#
.SYNOPSIS
  Alpixa icin Windows kurulum dosyasi (artifacts/AlpixaSetup.exe) uretir.

.DESCRIPTION
  1. Uygulamayi self-contained (kullanicinin .NET kurmasina gerek yok) olarak yayinlar.
  2. Inno Setup ile tek dosyalik kurulum programi olusturur.
  3. WINDOWS_SIGN_CERT ve WINDOWS_SIGN_PASSWORD ortam degiskenleri varsa kurulum dosyasini imzalar.

  Gereksinimler: Windows 10/11, .NET 10 SDK, MAUI workload, Inno Setup 6.

.EXAMPLE
  ./build/build-windows.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "win-x64"
$project = Join-Path $root "src/Alpixa.App/Alpixa.App.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK bulunamadi. https://dotnet.microsoft.com/download adresinden .NET 10 SDK kurun."
}

Write-Host "==> Uygulama yayinlaniyor ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project `
    -f net10.0-windows10.0.19041.0 `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:ApplicationDisplayVersion=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish basarisiz oldu." }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Inno Setup 6 bulunamadi. Kurmak icin: winget install JRSoftware.InnoSetup"
}

Write-Host "==> Kurulum dosyasi olusturuluyor..." -ForegroundColor Cyan
& $iscc "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" "/DAppVersion=$Version" (Join-Path $PSScriptRoot "Alpixa.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup basarisiz oldu." }

$setup = Join-Path $artifacts "AlpixaSetup.exe"

if ($env:WINDOWS_SIGN_CERT) {
    Write-Host "==> Kurulum dosyasi imzalaniyor..." -ForegroundColor Cyan
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } | Select-Object -Last 1
    if (-not $signtool) { throw "signtool.exe bulunamadi (Windows SDK gerekli)." }
    & $signtool.FullName sign /f $env:WINDOWS_SIGN_CERT /p $env:WINDOWS_SIGN_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $setup
    if ($LASTEXITCODE -ne 0) { throw "Imzalama basarisiz oldu." }
} else {
    Write-Host "Not: WINDOWS_SIGN_CERT tanimli degil; kurulum dosyasi imzasiz. Kullanicilar 'Bilinmeyen yayinci' uyarisi gorecek." -ForegroundColor Yellow
}

Write-Host "==> Hazir: $setup" -ForegroundColor Green

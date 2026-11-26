param(
    [string]$Version = '1.0.2'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$versionNumber = $Version.TrimStart('v')
$tag = "v$versionNumber"
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$installerDir = Join-Path $repoRoot 'artifacts\installer'
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue

if (-not $iscc) {
    throw 'Inno Setup Compiler (ISCC.exe) tidak ditemukan. Install Inno Setup untuk membuat installer secara lokal.'
}

& (Join-Path $PSScriptRoot 'Build-Clipper98.ps1') -Version $versionNumber
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
$installerPath = Join-Path $installerDir "Clipper98-Setup-$tag.exe"
& $iscc.Source "/DAppVersion=$versionNumber" `
    (Join-Path $repoRoot 'installer\Clipper98.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup gagal membuat installer (exit code $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer tidak ditemukan setelah build: $installerPath"
}

$size = (Get-Item -LiteralPath $installerPath).Length
Write-Host "Installer siap: $installerPath ($([math]::Round($size / 1MB)) MiB)."
Write-Host 'Paket tidak memuat yt-dlp, FFmpeg, Whisper CLI, atau model Whisper; Clipper mengunduhnya sesuai kebutuhan di PC pengguna.'

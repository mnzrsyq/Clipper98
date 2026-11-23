param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$folderName = "Clipper98-$tag"
$stage = Join-Path $repoRoot $folderName

if (-not (Test-Path -LiteralPath (Join-Path $stage 'Clipper98.exe'))) {
    & (Join-Path $PSScriptRoot 'Build-Clipper98.ps1') -Version $tag
}

$required = @(
    'tools\yt-dlp.exe',
    'tools\node.exe',
    'tools\ffmpeg.exe',
    'tools\whisper-cli.exe',
    'tools\python\python.exe',
    'tools\models\ggml-large-v3-turbo.bin'
)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $stage $_)) }
if ($missing) {
    & (Join-Path $PSScriptRoot 'Download-Tools.ps1') -TargetDir $stage
} else {
    Write-Host 'Tools dan model sudah ada di folder lokal; unduhan dilewati.'
}

$model = Join-Path $stage 'tools\models\ggml-large-v3-turbo.bin'
if ((Get-Item -LiteralPath $model).Length -lt 1400000000) {
    throw 'Ukuran model Large-v3-Turbo tidak sesuai; folder release belum siap.'
}

Write-Host "Folder Clipper98 lokal siap: $stage"
Write-Host 'Tidak ada tools, model, ZIP, atau executable yang diunggah ke GitHub.'

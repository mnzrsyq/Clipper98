param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$folderName = "Clipper98-$tag"
$stage = Join-Path $repoRoot $folderName

& (Join-Path $PSScriptRoot 'Build-Clipper98.ps1') -Version $tag

& (Join-Path $PSScriptRoot 'Download-Tools.ps1') -TargetDir $stage

$model = Join-Path $stage 'tools\models\ggml-large-v3-turbo.bin'
if ((Get-Item -LiteralPath $model).Length -lt 1400000000) {
    throw 'Ukuran model Large-v3-Turbo tidak sesuai; folder release belum siap.'
}

Write-Host "Folder Clipper98 lokal siap: $stage"
Write-Host 'Tidak ada tools, model, ZIP, atau executable yang diunggah ke GitHub.'

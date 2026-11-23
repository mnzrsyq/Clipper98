param(
    [string]$Version = '1.0.0',
    [switch]$SkipUpload
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$folderName = "Clipper98-$tag"
$stage = Join-Path $repoRoot $folderName
$archive = Join-Path $repoRoot "$folderName.zip"

if (Test-Path -LiteralPath $archive) { throw "Arsip sudah ada: $archive. Pindahkan atau hapus manual sebelum membuat ulang." }
if (-not (Get-Command gh -ErrorAction SilentlyContinue) -and -not $SkipUpload) {
    throw 'GitHub CLI (gh) tidak ditemukan. Pasang gh atau jalankan dengan -SkipUpload untuk hanya menyiapkan folder dan ZIP.'
}

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
    Write-Host 'Paket tools lengkap sudah tersedia; memakai salinan ini.'
}

Write-Host "Membuat ZIP $folderName ..."
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $archive)) { throw 'ZIP rilis tidak berhasil dibuat.' }

if (-not $SkipUpload) {
    & gh release create $tag $archive --repo 'mnzrsyq/Clipper98' --title "Clipper98 $tag" --notes "Rilis Windows x64. Ekstrak ZIP dan baca HOW-TO-USE.md. Paket ini menyertakan model Whisper Large-v3-Turbo reguler saja."
    if ($LASTEXITCODE -ne 0) { throw "Upload GitHub Release gagal dengan exit code $LASTEXITCODE." }
}

Write-Host "Selesai. Folder: $stage"
Write-Host "Arsip: $archive"

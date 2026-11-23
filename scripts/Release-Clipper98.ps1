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
$modelAsset = Join-Path $stage 'tools\models\ggml-large-v3-turbo.bin'

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
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveStream = [IO.File]::Open($archive, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$zip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($stage.TrimEnd('\').Length + 1).Replace('\', '/')
        if ($relativePath -ieq 'tools/models/ggml-large-v3-turbo.bin' -or $_.Extension -ieq '.pdb') { return }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $_.FullName, $relativePath, [IO.Compression.CompressionLevel]::NoCompression
        ) | Out-Null
    }
}
finally {
    $zip.Dispose()
}
if (-not (Test-Path -LiteralPath $archive)) { throw 'ZIP rilis tidak berhasil dibuat.' }

if (-not $SkipUpload) {
    & gh release view $tag --repo 'mnzrsyq/Clipper98' *> $null
    $releaseExists = $LASTEXITCODE -eq 0
    if ($releaseExists) {
        & gh release upload $tag $archive $modelAsset --repo 'mnzrsyq/Clipper98' --clobber
        if ($LASTEXITCODE -ne 0) { throw "Upload aset GitHub Release gagal dengan exit code $LASTEXITCODE." }
        & gh release edit $tag --repo 'mnzrsyq/Clipper98' --draft=false --title "Clipper98 $tag" --notes "Rilis Windows x64. Unduh ZIP aplikasi dan model ggml-large-v3-turbo.bin. Letakkan model di tools\models lalu baca HOW-TO-USE.md."
    } else {
        & gh release create $tag $archive $modelAsset --repo 'mnzrsyq/Clipper98' --title "Clipper98 $tag" --notes "Rilis Windows x64. Unduh ZIP aplikasi dan model ggml-large-v3-turbo.bin. Letakkan model di tools\models lalu baca HOW-TO-USE.md."
    }
    if ($LASTEXITCODE -ne 0) { throw "Pembuatan/publikasi GitHub Release gagal dengan exit code $LASTEXITCODE." }
}

Write-Host "Selesai. Folder: $stage"
Write-Host "Arsip: $archive"

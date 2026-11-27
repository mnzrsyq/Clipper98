param(
    [string]$TargetDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Clipper98-v1.1.3')
)

$ErrorActionPreference = 'Stop'
$toolsDir = Join-Path $TargetDir 'tools'
$modelsDir = Join-Path $toolsDir 'models'
$licensesDir = Join-Path $toolsDir 'licenses'
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ("Clipper98-tools-" + [guid]::NewGuid().ToString('N'))

function Get-Download([string]$Url, [string]$Destination) {
    Write-Host "Unduh $([IO.Path]::GetFileName($Destination)) ..."
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if (-not $curl) { throw 'curl.exe tidak ditemukan. Gunakan Windows 10/11 dengan curl tersedia di PATH.' }
    & $curl.Source --fail --location --retry 3 --retry-all-errors --retry-delay 5 --output $Destination $Url
    if ($LASTEXITCODE -ne 0) { throw "Unduhan gagal dengan exit code $LASTEXITCODE`: $Url" }
    if (-not (Test-Path -LiteralPath $Destination) -or (Get-Item -LiteralPath $Destination).Length -eq 0) {
        throw "Unduhan kosong atau gagal: $Url"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $TargetDir 'Clipper98.exe'))) {
    throw "Clipper98.exe belum ada di $TargetDir. Jalankan Build-Clipper98.bat dahulu."
}
New-Item -ItemType Directory -Path $toolsDir, $modelsDir, $licensesDir, $tempDir -Force | Out-Null

try {
    Get-Download 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' (Join-Path $toolsDir 'yt-dlp.exe')
    Get-Download 'https://nodejs.org/dist/latest-v24.x/win-x64/node.exe' (Join-Path $toolsDir 'node.exe')
    $nodeVersion = (& (Join-Path $toolsDir 'node.exe') -p 'process.versions.node').Trim()
    Get-Download 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/LICENSE' (Join-Path $licensesDir 'yt-dlp-LICENSE.txt')
    Get-Download "https://raw.githubusercontent.com/nodejs/node/v$nodeVersion/LICENSE" (Join-Path $licensesDir 'Node.js-LICENSE.txt')

    $ffmpegZip = Join-Path $tempDir 'ffmpeg.zip'
    Get-Download 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' $ffmpegZip
    $ffmpegExtract = Join-Path $tempDir 'ffmpeg'
    Expand-Archive -LiteralPath $ffmpegZip -DestinationPath $ffmpegExtract
    $ffmpeg = Get-ChildItem -LiteralPath $ffmpegExtract -Filter 'ffmpeg.exe' -File -Recurse | Select-Object -First 1
    if (-not $ffmpeg) { throw 'ffmpeg.exe tidak ditemukan dalam arsip FFmpeg.' }
    Copy-Item -LiteralPath $ffmpeg.FullName -Destination (Join-Path $toolsDir 'ffmpeg.exe') -Force
    Get-Download 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.GPLv3' (Join-Path $licensesDir 'FFmpeg-GPLv3.txt')

    $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=30' -Headers @{ 'User-Agent' = 'Clipper98-Release' }
    $release = $null
    $asset = $null
    foreach ($candidate in $releases) {
        if ($candidate.draft) { continue }
        $candidateAsset = $candidate.assets | Where-Object { $_.name -eq 'whisper-bin-x64.zip' } | Select-Object -First 1
        if ($candidateAsset) {
            $release = $candidate
            $asset = $candidateAsset
            break
        }
    }
    if (-not $release -or -not $asset) { throw 'Rilis whisper.cpp terbaru tidak menyediakan whisper-bin-x64.zip.' }
    $whisperTag = $release.tag_name
    $whisperZip = Join-Path $tempDir 'whisper.zip'
    Get-Download $asset.browser_download_url $whisperZip
    $whisperExtract = Join-Path $tempDir 'whisper'
    Expand-Archive -LiteralPath $whisperZip -DestinationPath $whisperExtract
    $whisperExe = Get-ChildItem -LiteralPath $whisperExtract -Filter 'whisper-cli.exe' -File -Recurse | Select-Object -First 1
    if (-not $whisperExe) { throw 'whisper-cli.exe tidak ditemukan dalam arsip whisper.cpp.' }
    Copy-Item -LiteralPath $whisperExe.FullName -Destination (Join-Path $toolsDir 'whisper-cli.exe') -Force
    Get-ChildItem -LiteralPath $whisperExtract -Filter '*.dll' -File -Recurse | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $toolsDir $_.Name) -Force
    }
    Get-Download "https://raw.githubusercontent.com/ggml-org/whisper.cpp/$whisperTag/LICENSE" (Join-Path $licensesDir 'whisper.cpp-MIT.txt')

    $modelPath = Join-Path $modelsDir 'ggml-large-v3-turbo.bin'
    $modelReady = (Test-Path -LiteralPath $modelPath) -and (Get-Item -LiteralPath $modelPath).Length -ge 1400000000
    if (-not $modelReady) {
        Get-Download 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin' $modelPath
    }
    if ((Get-Item -LiteralPath $modelPath).Length -lt 1400000000) {
        throw 'Ukuran model Large-v3-Turbo tidak sesuai. Periksa unduhan sebelum menjalankan aplikasi.'
    }

    $faceModel = Join-Path $modelsDir 'haarcascade_frontalface_default.xml'
    Get-Download 'https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml' $faceModel
    if ((Get-Item -LiteralPath $faceModel).Length -lt 500000) {
        throw 'Ukuran model face detector OpenCV tidak sesuai. Periksa unduhan sebelum menjalankan aplikasi.'
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'models\LICENSE-OpenCV.txt') -Destination (Join-Path $licensesDir 'OpenCV-LICENSE.txt') -Force

    @(
        'yt-dlp: latest official Windows x64 release',
        'FFmpeg: Gyan.dev essentials Windows build',
        "Node.js: v$nodeVersion Windows x64 node.exe",
        "whisper.cpp: $whisperTag CPU Windows x64",
        'Whisper model: ggml-large-v3-turbo.bin',
        'OpenCV: haarcascade_frontalface_default.xml'
    ) | Set-Content -LiteralPath (Join-Path $TargetDir 'TOOLS-VERSIONS.txt') -Encoding utf8

    Write-Host "Tools terbaru siap di $toolsDir. Model yang digunakan hanya ggml-large-v3-turbo.bin."
}
finally {
    if (Test-Path -LiteralPath $tempDir) { Remove-Item -LiteralPath $tempDir -Recurse -Force }
}

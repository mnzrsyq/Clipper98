param(
    [string]$TargetDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Clipper98-v1.0.0')
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

    $whisperTag = 'b5454'
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/whisper.cpp/releases/tags/$whisperTag" -Headers @{ 'User-Agent' = 'Clipper98-Release' }
    $asset = $release.assets | Where-Object { $_.name -eq 'whisper-bin-x64.zip' } | Select-Object -First 1
    if (-not $asset) { throw "Asset whisper-bin-x64.zip tidak ditemukan pada whisper.cpp $whisperTag." }
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

    $pythonVersion = '3.14.10'
    $pythonZip = Join-Path $tempDir 'python-embed.zip'
    Get-Download "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip" $pythonZip
    $pythonDir = Join-Path $toolsDir 'python'
    New-Item -ItemType Directory -Path $pythonDir -Force | Out-Null
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDir -Force
    if (-not (Test-Path -LiteralPath (Join-Path $pythonDir 'python.exe'))) { throw 'python.exe tidak ditemukan dalam paket Python portable.' }
    Copy-Item -LiteralPath (Join-Path $pythonDir 'LICENSE.txt') -Destination (Join-Path $licensesDir 'Python-PSF-LICENSE.txt') -Force

    $modelPath = Join-Path $modelsDir 'ggml-large-v3-turbo.bin'
    Get-Download 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin' $modelPath
    if ((Get-Item -LiteralPath $modelPath).Length -lt 1400000000) {
        throw 'Ukuran model Large-v3-Turbo tidak sesuai. Periksa unduhan sebelum menjalankan aplikasi.'
    }

    @(
        'yt-dlp: latest official Windows x64 release',
        'FFmpeg: Gyan.dev essentials Windows build',
        "Node.js: v$nodeVersion Windows x64 node.exe",
        "whisper.cpp: $whisperTag CPU Windows x64",
        "Python embeddable: $pythonVersion x64",
        'Whisper model: ggml-large-v3-turbo.bin (regular, not Q8_0)'
    ) | Set-Content -LiteralPath (Join-Path $TargetDir 'TOOLS-VERSIONS.txt') -Encoding utf8

    Write-Host "Tools siap di $toolsDir. Model yang diunduh hanya ggml-large-v3-turbo.bin."
}
finally {
    if (Test-Path -LiteralPath $tempDir) { Remove-Item -LiteralPath $tempDir -Recurse -Force }
}

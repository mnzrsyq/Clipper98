param(
    [string]$Version = '1.1.2'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$versionNumber = $Version.TrimStart('v')
$publishDir = Join-Path $repoRoot 'artifacts\publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK tidak ditemukan. Build lokal perlu .NET 8 SDK; pengguna installer tidak perlu memasang .NET.'
}

if (Test-Path -LiteralPath $publishDir) {
    $resolvedPublish = [IO.Path]::GetFullPath($publishDir)
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
    if (-not $resolvedPublish.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Folder publish di luar artifacts: $resolvedPublish"
    }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Mempublish Clipper98 $versionNumber sebagai aplikasi mandiri Windows x64..."
& dotnet publish (Join-Path $repoRoot 'Clipper98.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$versionNumber -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal dengan exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'Clipper98.exe'))) {
    throw 'Publish selesai tetapi Clipper98.exe tidak ditemukan.'
}

$forbidden = @(
    'tools\yt-dlp.exe',
    'tools\ffmpeg.exe',
    'tools\whisper-cli.exe',
    'tools\models\ggml-large-v3-turbo.bin'
)
foreach ($relativePath in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $publishDir $relativePath)) {
        throw "Paket installer tidak boleh menyertakan tools/model unduhan: $relativePath"
    }
}

$totalBytes = (Get-ChildItem -LiteralPath $publishDir -File -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host "Publish siap: $publishDir ($([math]::Round($totalBytes / 1MB)) MiB)."

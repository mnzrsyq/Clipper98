param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$folderName = "Clipper98-$tag"
$output = Join-Path $repoRoot $folderName

if (Test-Path -LiteralPath $output) {
    throw "Folder output sudah ada: $output. Pindahkan atau hapus folder itu secara manual sebelum build ulang."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet tidak ditemukan. Pasang .NET 8 SDK x64 terlebih dahulu.'
}

Write-Host "Membangun aplikasi ke $folderName ..."
& dotnet publish (Join-Path $repoRoot 'Clipper98.csproj') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal dengan exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $output 'Clipper98.exe'))) {
    throw 'Build selesai tetapi Clipper98.exe tidak ditemukan.'
}

Write-Host "Build selesai: $output"

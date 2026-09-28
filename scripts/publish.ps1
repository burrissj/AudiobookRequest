# Builds the React client, publishes the API as a self-contained win-x64 exe with the client
# baked into wwwroot, and bundles ffmpeg/ffprobe alongside it. Run this before compiling the
# Inno Setup installer (installer/AudiobookRequest.iss), which packages the ./publish output.
#
# Usage: powershell -File scripts/publish.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $root "client"
$apiDir = Join-Path $root "src\AudiobookRequest.Api"
$wwwroot = Join-Path $apiDir "wwwroot"
$publishDir = Join-Path $root "publish"
$ffmpegToolsDir = Join-Path $root "tools\ffmpeg"

if (-not (Test-Path (Join-Path $ffmpegToolsDir "ffmpeg.exe"))) {
    throw "Missing $ffmpegToolsDir\ffmpeg.exe. Download the ffmpeg essentials build and place ffmpeg.exe/ffprobe.exe there first."
}

Write-Host "==> Building client..." -ForegroundColor Cyan
Push-Location $clientDir
try {
    if (-not (Test-Path "node_modules")) { npm install }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "client build failed" }
} finally {
    Pop-Location
}

Write-Host "==> Copying client build into wwwroot..." -ForegroundColor Cyan
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item (Join-Path $clientDir "dist") $wwwroot -Recurse

Write-Host "==> Publishing self-contained win-x64..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $apiDir -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Bundling ffmpeg/ffprobe..." -ForegroundColor Cyan
$publishTools = Join-Path $publishDir "tools"
New-Item -ItemType Directory -Force -Path $publishTools | Out-Null
Copy-Item (Join-Path $ffmpegToolsDir "ffmpeg.exe") $publishTools -Force
Copy-Item (Join-Path $ffmpegToolsDir "ffprobe.exe") $publishTools -Force

Write-Host "==> Publish complete: $publishDir" -ForegroundColor Green
Write-Host "    Next: compile installer\AudiobookRequest.iss with Inno Setup." -ForegroundColor Green

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptRoot
$project = Join-Path $root "TWWH3SoloMp.csproj"
$dist = Join-Path $root "dist"

if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $dist

Write-Host ""
Write-Host "Published files:"
Get-ChildItem -LiteralPath $dist | Select-Object Name, Length

Write-Host ""
Write-Host "Release artifact:"
$artifact = Join-Path $dist "twwh3-solo-mp-patcher.exe"
Write-Host $artifact

if (Test-Path -LiteralPath $artifact) {
    $artifactHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ""
    Write-Host "Release notes:"
    Write-Host "Artifact: twwh3-solo-mp-patcher.exe"
    Write-Host "SHA-256: $artifactHash"
}

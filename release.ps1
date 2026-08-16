# EdgeShelf release packaging script (ASCII-only; PS 5.1 cannot parse UTF-8 Chinese comments)
# Usage:
#   .\release.ps1 -Version 1.11.0
# Steps: bump version -> publish self-contained exe + framework-dependent exe -> create win-x64 zip
# Source code is NOT zipped: GitHub auto-packs source archives from tags.
# Then (manually): git commit & push, gh release create (command printed at the end).

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $PSScriptRoot 'EdgeShelf\EdgeShelf.csproj'
$nugetConfig = Join-Path $root 'running\nuget.config'

$env:APPDATA          = Join-Path $root 'running\.appdata'
$env:NUGET_PACKAGES   = Join-Path $root 'running\.nuget'
$env:DOTNET_CLI_HOME  = Join-Path $root 'running\.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:NUGET_PACKAGES, $env:DOTNET_CLI_HOME | Out-Null

# 1. bump version in csproj
$csproj = Get-Content $proj -Raw
$csproj = $csproj -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>"
Set-Content $proj $csproj -Encoding UTF8
Write-Host "==> Version bumped to $Version" -ForegroundColor Cyan

# 2. publish self-contained single-file exe (bundles .NET 8, no install needed)
Write-Host "==> Publishing self-contained exe ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    --configfile $nugetConfig `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $PSScriptRoot 'publish\win-x64')

# 2b. publish framework-dependent single-file exe (requires .NET 8 Desktop Runtime, smaller)
Write-Host "==> Publishing framework-dependent exe ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained false `
    --configfile $nugetConfig `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $PSScriptRoot 'publish\win-x64-fx')

# 3. copy exes + create win-x64 zip (beta naming: EdgeShelf-betaX.Y.Z-*)
$exe = Join-Path $PSScriptRoot 'EdgeShelf.exe'
$fxExe = Join-Path $PSScriptRoot 'EdgeShelf-net8.exe'
Copy-Item (Join-Path $PSScriptRoot 'publish\win-x64\EdgeShelf.exe') $exe -Force
Copy-Item (Join-Path $PSScriptRoot 'publish\win-x64-fx\EdgeShelf.exe') $fxExe -Force
$winZip = Join-Path $PSScriptRoot "EdgeShelf-beta$Version-win-x64.zip"
Remove-Item $winZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $exe -DestinationPath $winZip -Force

Write-Host "==> Done. Artifacts:" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  $fxExe"
Write-Host "  $winZip"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  cd '$PSScriptRoot'"
Write-Host "  git add . ; git commit -m 'EdgeShelf v$Version' ; git push"
Write-Host "  gh release create v$Version $exe $fxExe $winZip --title 'EdgeShelf beta$Version' --prerelease --notes 'see README'"
Write-Host ""
Write-Host "Note: update README version badge / changelog before commit if needed."

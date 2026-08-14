# EdgeShelf release packaging script
# Usage:
#   .\release.ps1 -Version 1.5.0
# Steps: bump version in csproj -> publish single-file exe -> create win-x64 zip + source zip
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

# 2. publish self-contained single-file exe
Write-Host "==> Publishing (needs network for runtime pack on first run) ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    --configfile $nugetConfig `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $PSScriptRoot 'publish\win-x64')

# 3. copy exe + create win-x64 zip
$exe = Join-Path $PSScriptRoot 'EdgeShelf.exe'
Copy-Item (Join-Path $PSScriptRoot 'publish\win-x64\EdgeShelf.exe') $exe -Force
$winZip = Join-Path $PSScriptRoot "EdgeShelf-$Version-win-x64.zip"
$srcZip = Join-Path $PSScriptRoot "EdgeShelf-$Version-source.zip"
Remove-Item $winZip, $srcZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $exe -DestinationPath $winZip -Force

# 4. create source zip (exclude bin/obj)
$stage = Join-Path $PSScriptRoot '.src-staging'
$pkg = Join-Path $stage "EdgeShelf-$Version"
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'build.ps1') $pkg
Copy-Item (Join-Path $PSScriptRoot 'EdgeShelf') (Join-Path $pkg 'EdgeShelf') -Recurse
Remove-Item (Join-Path $pkg 'EdgeShelf\bin'), (Join-Path $pkg 'EdgeShelf\obj') -Recurse -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $pkg -DestinationPath $srcZip -Force
Remove-Item $stage -Recurse -Force

Write-Host "==> Done. Artifacts:" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  $winZip"
Write-Host "  $srcZip"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  cd '$PSScriptRoot'"
Write-Host "  git add . ; git commit -m 'EdgeShelf v$Version' ; git push"
Write-Host "  gh release create v$Version EdgeShelf.exe $winZip $srcZip --title 'EdgeShelf v$Version' --notes 'see README'"
Write-Host ""
Write-Host "Note: update README version badge / changelog before commit if needed."

# EdgeShelf build script
# Usage:
#   .\build.ps1            # build (Debug)
#   .\build.ps1 -Publish   # publish self-contained single-file exe (needs network)
#   .\build.ps1 -Clean     # clean bin/obj first
#
# Layout (sibling of release\):
#   release\EdgeShelf\    source code
#   running\.nuget\       NuGet cache (avoid writing to user profile)
#   running\.dotnet\      dotnet CLI data
#   running\nuget.config  NuGet config used for building

param(
    [switch]$Publish,
    [switch]$Clean
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $PSScriptRoot 'EdgeShelf\EdgeShelf.csproj'
$nugetConfig = Join-Path $root 'running\nuget.config'

# Redirect NuGet cache / CLI data into running\ so nothing is written to the user profile.
$env:APPDATA             = Join-Path $root 'running\.appdata'
$env:NUGET_PACKAGES      = Join-Path $root 'running\.nuget'
$env:DOTNET_CLI_HOME     = Join-Path $root 'running\.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:NUGET_PACKAGES, $env:DOTNET_CLI_HOME | Out-Null

if ($Clean) {
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot 'EdgeShelf\bin') -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot 'EdgeShelf\obj') -ErrorAction SilentlyContinue
}

if ($Publish) {
    Write-Host "==> Publish self-contained (win-x64) ..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        --configfile $nugetConfig `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o (Join-Path $PSScriptRoot 'publish\win-x64')
    Write-Host "==> Done: publish\win-x64\EdgeShelf.exe (copy it into release\)" -ForegroundColor Green
} else {
    Write-Host "==> Build (Debug) ..." -ForegroundColor Cyan
    dotnet build $proj -c Debug --configfile $nugetConfig
}

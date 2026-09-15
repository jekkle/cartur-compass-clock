# Publishes the already-packed zip to Thunderstore.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
#
# Separate from pack.ps1 on purpose. Packing is repeatable and harmless; publishing
# is neither - Thunderstore has no un-publish, and a version number can never be
# reused. Keeping them apart means nobody ships a package by running a build.
#
# The token is read from the TCLI_AUTH_TOKEN environment variable and is never
# written to disk here. Generate one at:
#   thunderstore.io -> Settings -> Teams -> Cartur -> Service Accounts
# and set it for the session only:
#   $env:TCLI_AUTH_TOKEN = "tss_..."

[CmdletBinding()]
param(
    # Prints what would be published and stops. Run this first.
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# The version comes from the manifest, not from thunderstore.toml, so there is only
# one place a version is ever edited. pack.ps1 has already checked that this agrees
# with Plugin.cs.
$manifest = Get-Content (Join-Path $root "package\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
$zip = Join-Path $root "dist\$($manifest.name)-$version.zip"

if (-not (Test-Path $zip)) { throw "$zip not found - run tools\pack.ps1 first." }

# Published zips are immutable, so a zip left over in dist/ from before the last edit
# must not be the thing that ships. Timestamps, not hashes: two Release builds of the
# same source are not byte-identical, so comparing the packed DLL against a later
# rebuild reports drift that does not exist. Asking "was anything changed after this
# was packed" is the question actually worth answering.
$newest = Get-ChildItem (Join-Path $root "src") -Recurse -File -Include *.cs, *.csproj, *.png |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newest -and $newest.LastWriteTime -gt (Get-Item $zip).LastWriteTime) {
    throw "$($newest.Name) was changed after $($manifest.name)-$version.zip was packed - run tools\pack.ps1 again."
}

$tcli = Join-Path $env:USERPROFILE ".dotnet\tools\tcli.exe"
if (-not (Test-Path $tcli)) {
    if (Get-Command tcli -ErrorAction SilentlyContinue) { $tcli = "tcli" }
    else { throw "tcli not found. Install it with:  dotnet tool install -g tcli" }
}

Write-Host "package : $($manifest.name) $version"
Write-Host "file    : $zip"
Write-Host "size    : $([math]::Round((Get-Item $zip).Length / 1KB)) KB"

if ($WhatIf) { Write-Host "WhatIf - nothing published."; return }

if (-not $env:TCLI_AUTH_TOKEN) {
    throw "TCLI_AUTH_TOKEN is not set. See the header of this script for where to generate one."
}

# --file, so the package tested locally is the exact package published. Without it
# tcli builds its own from thunderstore.toml, which would be a second packer to keep
# in step with pack.ps1.
#
# No --package-version: tcli rejects it alongside --file, because with --file the
# version is read from the manifest.json inside the zip. That is the answer we want
# anyway - the version travels with the package instead of being asserted beside it.
& $tcli publish --config-path (Join-Path $root "thunderstore.toml") --file $zip
if ($LASTEXITCODE -ne 0) { throw "tcli publish failed with exit code $LASTEXITCODE" }

Write-Host "published $($manifest.name) $version"
Write-Host "Now run tools\publish-nexus.ps1 for the Nexus side."

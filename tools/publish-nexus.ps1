# Publishes the already-packed Nexus zip to nexusmods.com, replacing the browser form.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-nexus.ps1 -WhatIf
#   powershell -ExecutionPolicy Bypass -File tools\publish-nexus.ps1
#
# Companion to publish.ps1, which does the same for Thunderstore. Same shape: packing
# stays in pack.ps1, this only uploads what was packed, and -WhatIf prints the plan
# without sending anything that changes the site.
#
# The key is read from NEXUS_API_KEY and is never written to disk here. Get one at
#   https://www.nexusmods.com/settings/api-keys
# and set it for good:
#   [Environment]::SetEnvironmentVariable('NEXUS_API_KEY','<key>','User')
#
# API v3, documented at https://api-docs.nexusmods.com. The upload is four steps:
#   POST /uploads                  -> upload id + presigned S3 URL
#   PUT  <presigned url>           -> the bytes
#   POST /uploads/{id}/finalise    -> close the session
#   POST /mod-files/{id}/versions  -> turn it into a new version of the main file
# then POST /mods/{id}/changelogs for the changelog.
#
# Note on stability: /uploads is a stable endpoint, but the mod-file endpoints carry
# Nexus's own "Experimental" badge - they may change or be withdrawn. If a release
# fails here, the site's upload form still works and is the fallback.

[CmdletBinding()]
param(
    # Print what would be published and stop. Nothing that changes the site is sent,
    # though it does read the mod's files to resolve ids.
    [switch]$WhatIf,

    # Nexus game domain, as it appears in the site URL.
    [string]$Game = "valheim",

    # The mod id from the site URL: nexusmods.com/valheim/mods/3681
    [string]$ModId = "3684",

    # Which of the mod's files this is a new version of. Matched on name.
    [string]$FileName = "Cartur's Compass and Clock"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$api = "https://api.nexusmods.com/v3"

if (-not $env:NEXUS_API_KEY) {
    throw "NEXUS_API_KEY is not set. See the header of this script for where to get one."
}
$headers = @{ apikey = $env:NEXUS_API_KEY; "Content-Type" = "application/json" }

$manifest = Get-Content (Join-Path $root "package\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
$zip = Join-Path $root "dist\$($manifest.name)-$version-Nexus.zip"
if (-not (Test-Path $zip)) { throw "$zip not found - run tools\pack.ps1 first." }

# Same staleness check as publish.ps1: a zip packed before the last edit must not ship.
$newest = Get-ChildItem (Join-Path $root "src") -Recurse -File -Include *.cs, *.csproj, *.png |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newest -and $newest.LastWriteTime -gt (Get-Item $zip).LastWriteTime) {
    throw "$($newest.Name) was changed after $(Split-Path $zip -Leaf) was packed - run tools\pack.ps1 again."
}

$file = Get-Item $zip

# Both forms of the same digest: hex for the JSON body, base64 for the Content-MD5
# header on the PUT. Computed once from the raw bytes rather than converted back and
# forth from a string, which is where this sort of thing usually goes wrong.
$md5Bytes = [System.Security.Cryptography.MD5]::Create().ComputeHash([System.IO.File]::ReadAllBytes($zip))
$md5 = ([BitConverter]::ToString($md5Bytes) -replace '-', '').ToLower()
$md5Base64 = [Convert]::ToBase64String($md5Bytes)

# The changelog for this version, lifted from CHANGELOG.md so it is written once.
$changelogPath = Join-Path $root "package\CHANGELOG.md"
$changelog = ""
if (Test-Path $changelogPath) {
    # Everything between this version's "## x.y.z" heading and the next "## " heading.
    $collecting = $false
    $body = @()
    # -Encoding UTF8 is load-bearing: without it Get-Content reads the file as ANSI, so an
    # em dash comes back as three CP1252 characters. That mojibake then goes out in the JSON
    # body and Nexus rejects the whole request as malformed - which published the file but
    # silently lost its changelog.
    foreach ($line in Get-Content $changelogPath -Encoding UTF8) {
        if ($line -match '^##\s+(.+?)\s*$') {
            if ($collecting) { break }
            $collecting = ($Matches[1] -eq $version)
            continue
        }
        if ($collecting) { $body += $line }
    }
    $changelog = ($body -join "`n").Trim()
}

# --- resolve the ids -------------------------------------------------------------
# The site URL carries a game-scoped id; every write endpoint wants the internal one.
$mod = Invoke-RestMethod -Method Get -Uri "$api/games/$Game/mods/$ModId" -Headers $headers
$modUid = $mod.data.id

$files = Invoke-RestMethod -Method Get -Uri "$api/mods/$modUid/files" -Headers $headers
$target = $files.data.mod_files | Where-Object { $_.name -eq $FileName } | Select-Object -First 1
if (-not $target) {
    $names = ($files.data.mod_files | ForEach-Object { "'" + $_.name + "'" }) -join ", "
    throw "No mod file called '$FileName'. Found: $names"
}

Write-Host "mod      : $($mod.data.name)  ($Game/$ModId -> $modUid)"
Write-Host "file     : $($target.name)  ($($target.id))"
Write-Host "version  : $version"
Write-Host "archive  : $($file.Name)  $([math]::Round($file.Length / 1KB)) KB"
Write-Host "md5      : $md5"
Write-Host "changelog: $(if ($changelog) { "$(($changelog -split "`n").Count) line(s) from CHANGELOG.md" } else { 'NONE FOUND - check the heading matches the version' })"

if ($WhatIf) { Write-Host "WhatIf - nothing uploaded."; return }

# --- 1. open an upload session ---------------------------------------------------
# md5 is optional until 2026-12-01 and required after it. Sent now so this does not
# quietly break on that date, and because it binds the presigned URL to these bytes.
$session = Invoke-RestMethod -Method Post -Uri "$api/uploads" -Headers $headers -Body (@{
    size_bytes = $file.Length
    filename   = $file.Name
    md5        = $md5
} | ConvertTo-Json)

$uploadId = $session.data.id
Write-Host "upload   : $uploadId"

# --- 2. put the bytes ------------------------------------------------------------
# Content-Disposition and Content-MD5 are part of the presigned URL's signature, so
# storage rejects the upload outright if either is missing or does not match.
Invoke-RestMethod -Method Put -Uri $session.data.presigned_url -InFile $zip -Headers @{
    "Content-Disposition" = "attachment; filename=`"$($file.Name)`""
    "Content-MD5"         = $md5Base64
} -ContentType "application/octet-stream" | Out-Null
Write-Host "uploaded : $([math]::Round($file.Length / 1KB)) KB"

# --- 3. close the session and wait for it to be processed ------------------------
Invoke-RestMethod -Method Post -Uri "$api/uploads/$uploadId/finalise" -Headers $headers | Out-Null

$state = ""
foreach ($attempt in 1..60) {
    Start-Sleep -Seconds 2
    $state = (Invoke-RestMethod -Method Get -Uri "$api/uploads/$uploadId" -Headers $headers).data.state
    if ($state -eq "available") { break }
    if ($state -eq "failed") { throw "Nexus rejected the upload (state: failed)." }
}
if ($state -ne "available") { throw "Upload never became available (last state: $state)." }
Write-Host "state    : available"

# --- 4. make it a new version of the main file -----------------------------------
# update_mod_version and archive_existing_file are the two tick boxes on the form:
# bump the mod's version to match, and move the previous file to the archive.
$created = Invoke-RestMethod -Method Post -Uri "$api/mod-files/$($target.id)/versions" -Headers $headers -Body (@{
    upload_id             = $uploadId
    name                  = $target.name
    version               = $version
    file_category         = "main"
    update_mod_version    = $true
    archive_existing_file = $true
} | ConvertTo-Json)

Write-Host "published: $($target.name) $version"

# --- 5. the changelog ------------------------------------------------------------
# Additive on Nexus's side, so this runs last and only once per version.
if ($changelog) {
    # Sent as UTF-8 bytes rather than as a string: PowerShell 5.1 does not encode a string
    # body as UTF-8 by default, so anything outside ASCII arrives corrupted even when the
    # JSON it was built from was correct.
    $clJson = @{ version = $version; changelog = $changelog } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$api/mods/$modUid/changelogs" `
        -Headers @{ apikey = $env:NEXUS_API_KEY } `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($clJson)) | Out-Null
    Write-Host "changelog: added"
}

# Braces around ModId: PowerShell reads ? as part of a variable name, so "$ModId?tab"
# resolves to an empty variable rather than the id followed by a query string.
Write-Host "https://www.nexusmods.com/$Game/mods/${ModId}?tab=files"

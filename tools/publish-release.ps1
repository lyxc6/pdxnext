<#
.SYNOPSIS
  Publish the PdxNext installer and update manifest to a public GitHub release.

.DESCRIPTION
  Requires a token with repo scope for lyxc6/pdxnext:
    $env:GITHUB_TOKEN = "ghp_..."
  The repository must exist and be public. The script creates the release if
  needed, replaces the two assets, and uploads update.json last so
  releases/latest/download/update.json always points at a complete release.

.EXAMPLE
  .\tools\publish-release.ps1 -Version 1.0.0 -NotesFile .\docs\RELEASE-NOTES-1.0.0.md
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Tag = "",
    [string]$Repo = "lyxc6/pdxnext",
    [string]$InstallerDir = "dist\installer",
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$NotesFile = "",
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"

function Read-VersionFromProps {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "missing $Path" }
    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    return [string]$xml.Project.PropertyGroup.Version
}

if (-not $Version) { $Version = Read-VersionFromProps "version.props" }
if (-not $Tag) { $Tag = "v$Version" }
if (-not $Token) { throw "Set GITHUB_TOKEN or pass -Token." }

$setup = Join-Path $InstallerDir "PdxNext-Setup-$Version.exe"
$manifest = Join-Path $InstallerDir "update.json"
foreach ($file in @($setup, $manifest)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "missing $file; run tools/build-installer.py first" }
}

$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "PdxNext-Release"
}

$api = "https://api.github.com/repos/$Repo"
$release = $null
try {
    $release = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$Tag" -Headers $headers
} catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
}

if ($null -eq $release) {
    $body = @{
        tag_name               = $Tag
        name                   = "PdxNext $Version"
        body                   = if ($NotesFile -and (Test-Path -LiteralPath $NotesFile)) { Get-Content -LiteralPath $NotesFile -Raw } else { "PdxNext $Version" }
        draft                  = $false
        prerelease             = [bool]$Prerelease
        generate_release_notes = $false
    }
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -Body ($body | ConvertTo-Json -Depth 5) -ContentType "application/json"
    Write-Host "created release $Tag"
} else {
    Write-Host "release $Tag already exists; replacing assets"
    foreach ($asset in @($release.assets)) {
        if ($asset.name -eq (Split-Path $setup -Leaf) -or $asset.name -eq "update.json") {
            Invoke-RestMethod -Method Delete -Uri "$api/releases/assets/$($asset.id)" -Headers $headers
        }
    }
}

function Publish-Asset {
    param([string]$Path, [string]$Name)
    $upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$Name"
    $result = Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -InFile $Path -ContentType "application/octet-stream"
    Write-Host ("uploaded {0} ({1} bytes)" -f $Name, $result.size)
}

# Upload the installer first, then the manifest that references it.
Publish-Asset -Path $setup -Name (Split-Path $setup -Leaf)
Publish-Asset -Path $manifest -Name "update.json"

Write-Host "published https://github.com/$Repo/releases/tag/$Tag"

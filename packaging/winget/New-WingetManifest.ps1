<#
.SYNOPSIS
    Fills the winget manifest templates for one Looppaper release.

.DESCRIPTION
    Replaces the version, installer URL, SHA-256, release date and release notes URL in
    packaging/winget/templates/*.yaml and writes the result in the winget-pkgs layout:
    <OutputDirectory>\manifests\d\DenKu1\Looppaper\<Version>\

    The SHA-256 comes from -Sha256, from a local zip (-ZipPath), or by downloading the release's
    .sha256 file from the installer URL. winget itself downloads the installer from InstallerUrl, so the
    release asset must be publicly downloadable (a published release of this public repository).

    This script only writes files. It never submits anything to microsoft/winget-pkgs; to publish, open a
    pull request there yourself (for example with "wingetcreate submit <folder>") once the repository is public.

.EXAMPLE
    .\packaging\winget\New-WingetManifest.ps1 -Version 0.2.0 -Validate

.EXAMPLE
    .\packaging\winget\New-WingetManifest.ps1 -Version 0.2.0 -ZipPath .\artifacts\Looppaper-0.2.0-win-x64.zip
#>
[CmdletBinding()]
param(
    # Release version without the leading "v", e.g. 0.2.0.
    [Parameter(Mandatory)]
    [string]$Version,

    # GitHub repository that hosts the release.
    [string]$Repository = 'DenKu1/Looppaper',

    # SHA-256 of the release zip (hex). Takes precedence over -ZipPath and the downloaded .sha256 file.
    [string]$Sha256,

    # Local copy of the release zip to hash instead of downloading the .sha256 file.
    [string]$ZipPath,

    # Release date (yyyy-MM-dd); defaults to today (UTC).
    [string]$ReleaseDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),

    # Root of the generated manifests.
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out'),

    # Run "winget validate" on the result (needs winget).
    [switch]$Validate
)

$ErrorActionPreference = 'Stop'

$Version = $Version -replace '^v', ''
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not X.Y.Z or X.Y.Z-prerelease."
}
if ($ReleaseDate -notmatch '^\d{4}-\d{2}-\d{2}$') {
    throw "ReleaseDate '$ReleaseDate' is not yyyy-MM-dd."
}

$tag = "v$Version"
$zipName = "Looppaper-$Version-win-x64.zip"
$installerUrl = "https://github.com/$Repository/releases/download/$tag/$zipName"
$releaseNotesUrl = "https://github.com/$Repository/releases/tag/$tag"

if (-not $Sha256) {
    if ($ZipPath) {
        $Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash
    }
    else {
        try {
            $response = Invoke-WebRequest -Uri "$installerUrl.sha256" -UseBasicParsing
            $text = if ($response.Content -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
            $Sha256 = ($text.Trim() -split '\s+')[0]
        }
        catch {
            throw ("Cannot download $installerUrl.sha256 ($($_.Exception.Message)). The release must exist and be " +
                "publicly downloadable (a private repository's assets are not), or pass -Sha256 / -ZipPath.")
        }
    }
}

$Sha256 = $Sha256.Trim().ToUpperInvariant()
if ($Sha256 -notmatch '^[0-9A-F]{64}$') {
    throw "'$Sha256' is not a SHA-256 hash."
}

$target = Join-Path $OutputDirectory "manifests\d\DenKu1\Looppaper\$Version"
New-Item -ItemType Directory -Force -Path $target | Out-Null

$values = @{
    '__VERSION__'           = $Version
    '__INSTALLER_URL__'     = $installerUrl
    '__INSTALLER_SHA256__'  = $Sha256
    '__RELEASE_DATE__'      = $ReleaseDate
    '__RELEASE_NOTES_URL__' = $releaseNotesUrl
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
foreach ($template in Get-ChildItem -Path (Join-Path $PSScriptRoot 'templates') -Filter '*.yaml') {
    $text = [System.IO.File]::ReadAllText($template.FullName)
    foreach ($key in $values.Keys) {
        $text = $text.Replace($key, $values[$key])
    }
    if ($text -match '__[A-Z_]+__') {
        throw "$($template.Name) still contains the placeholder $($Matches[0])."
    }
    [System.IO.File]::WriteAllText((Join-Path $target $template.Name), $text, $utf8NoBom)
}

Write-Host "Wrote winget manifests for $Version to $target"
Write-Host "  InstallerUrl    $installerUrl"
Write-Host "  InstallerSha256 $Sha256"

if ($Validate) {
    winget validate --manifest $target
    if ($LASTEXITCODE -ne 0) {
        throw "winget validate failed ($LASTEXITCODE)."
    }
}

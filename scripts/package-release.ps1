<#
.SYNOPSIS
    Packs a published Looppaper.exe into the release zip and writes its SHA-256 file.

.DESCRIPTION
    Used by .github/workflows/release.yml, and runnable locally after
    "dotnet publish src/Looppaper -p:PublishProfile=win-x64". Creates
    Looppaper-<Version>-win-x64.zip with Looppaper.exe, install.ps1, uninstall.ps1, README.md and any
    license / notice files in the repository root, plus Looppaper-<Version>-win-x64.zip.sha256 in the
    "sha256sum" format ("<hash>  <file name>").

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    # Version without the leading "v", e.g. 0.2.0 or 0.3.0-beta.1.
    [Parameter(Mandatory)]
    [string]$Version,

    # Folder that contains the published Looppaper.exe.
    [string]$PublishDir = (Join-Path $PSScriptRoot '..\publish'),

    # Where the zip and .sha256 file are written.
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not X.Y.Z or X.Y.Z-prerelease."
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$exe = Join-Path $PublishDir 'Looppaper.exe'
if (-not (Test-Path $exe)) {
    throw "$exe not found. Publish first: dotnet publish src/Looppaper -p:PublishProfile=win-x64"
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("looppaper-package-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging, $OutputDir | Out-Null
try {
    $files = @(
        $exe,
        (Join-Path $root 'scripts\install.ps1'),
        (Join-Path $root 'scripts\uninstall.ps1'),
        (Join-Path $root 'README.md')
    )
    # License and notice files are optional: include whichever exist.
    $files += Get-ChildItem -Path $root -File |
        Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD-PARTY-NOTICES)(\..+)?$' } |
        ForEach-Object { $_.FullName }

    Copy-Item -LiteralPath $files -Destination $staging

    $zipName = "Looppaper-$Version-win-x64.zip"
    $zip = Join-Path $OutputDir $zipName
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
    # LF line ending and no BOM, so "sha256sum -c" and Get-Content both read it.
    [System.IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n", (New-Object System.Text.UTF8Encoding($false)))

    Write-Host "Packed $(($files | ForEach-Object { Split-Path $_ -Leaf }) -join ', ')"
    Write-Host "$zip"
    Write-Host "SHA256 $hash"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
    Installs Looppaper for the current user (no admin rights needed).

.DESCRIPTION
    Copies Looppaper.exe to the install folder (default %LOCALAPPDATA%\Programs\Looppaper),
    registers Looppaper to start when you sign in, and starts it. Run again to upgrade.

    Layout Portable (default): everything lives in the install folder:
        Looppaper.exe, config.json, collection\ (your pictures and videos), cache\, logs\
    On its first start Looppaper copies settings, cached videos and the pictures/videos of the old
    Pictures\Looppaper folder from a previous standard install (the originals are kept).

    Layout Standard: the Windows-style split:
        config in %APPDATA%\Looppaper, cache and logs in %LOCALAPPDATA%\Looppaper,
        collection in %USERPROFILE%\Pictures\Looppaper.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Layout Standard
#>
[CmdletBinding()]
param(
    # Path to Looppaper.exe. Defaults to the exe next to this script, or ..\publish\Looppaper.exe in a source checkout.
    [string]$Source,

    # Portable (default): everything in the install folder. Standard: config, cache and collection in the usual Windows folders.
    [ValidateSet('Portable', 'Standard')]
    [string]$Layout = 'Portable',

    # Where Looppaper.exe goes (and, in the portable layout, all of its files).
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Looppaper')
)

$ErrorActionPreference = 'Stop'

if (-not $Source) {
    $candidates = @(
        (Join-Path $PSScriptRoot 'Looppaper.exe'),
        (Join-Path $PSScriptRoot '..\publish\Looppaper.exe')
    )
    $Source = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Source) {
        throw "Looppaper.exe not found next to this script. Build it with: dotnet publish src/Looppaper -p:PublishProfile=win-x64"
    }
}

$exe = Join-Path $InstallDir 'Looppaper.exe'
$marker = Join-Path $InstallDir 'looppaper.portable'

if (Test-Path $exe) {
    Write-Host 'Stopping the running Looppaper (if any)...'
    Start-Process -FilePath $exe -ArgumentList '--stop' -Wait -WindowStyle Hidden
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -LiteralPath (Resolve-Path $Source) -Destination $exe -Force
Write-Host "Installed to $exe"

if ($Layout -eq 'Portable') {
    New-Item -ItemType File -Force -Path $marker | Out-Null
    $collection = Join-Path $InstallDir 'collection'
    $settings = Join-Path $InstallDir 'config.json'
    $logs = Join-Path $InstallDir 'logs\looppaper.log'
    Write-Host "Layout: portable - settings, collection, cache and logs all live in $InstallDir"
} else {
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    $collection = Join-Path $env:USERPROFILE 'Pictures\Looppaper'
    $settings = Join-Path $env:APPDATA 'Looppaper\config.json'
    $logs = Join-Path $env:LOCALAPPDATA 'Looppaper\logs\looppaper.log'
    Write-Host 'Layout: standard - settings in %APPDATA%\Looppaper, cache and logs in %LOCALAPPDATA%\Looppaper'
}

New-Item -ItemType Directory -Force -Path $collection | Out-Null
Write-Host "Collection folder: $collection (put .jpg/.jpeg/.png pictures or .mp4 videos here)"

$p = Start-Process -FilePath $exe -ArgumentList '--install-startup' -Wait -PassThru -WindowStyle Hidden
if ($p.ExitCode -ne 0) {
    throw "Registering Looppaper to start at sign-in failed (exit code $($p.ExitCode)). See $logs"
}
Write-Host 'Looppaper will start when you sign in.'

Start-Process -FilePath $exe
Write-Host "Looppaper is running. Settings: $settings (restart Looppaper after editing). Log: $logs"

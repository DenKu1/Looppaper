<#
.SYNOPSIS
    Installs Looppaper for the current user (no admin rights needed).

.DESCRIPTION
    Copies Looppaper.exe to the install folder (default %LOCALAPPDATA%\Programs\Looppaper),
    registers Looppaper to start when you sign in, and starts it. Run again to upgrade.
    Without -Layout it asks where Looppaper should keep its files:

    Standard (default): the usual Windows folders:
        config in %APPDATA%\Looppaper, cache and logs in %LOCALAPPDATA%\Looppaper,
        collection in %USERPROFILE%\Pictures\Looppaper.

    Portable: everything lives in the install folder:
        Looppaper.exe, config.json, collection\ (your pictures and videos), cache\, logs\
    On its first start Looppaper copies settings, cached videos and the pictures/videos of the
    Pictures\Looppaper folder from the standard folders (the originals are kept).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Layout Portable
#>
[CmdletBinding()]
param(
    # Path to Looppaper.exe. Defaults to the exe next to this script, or ..\publish\Looppaper.exe in a source checkout.
    [string]$Source,

    # Standard (default): config, cache and collection in the usual Windows folders. Portable: everything in the install folder.
    # Leave it out to be asked.
    [ValidateSet('Standard', 'Portable')]
    [string]$Layout,

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

if (-not $Layout) {
    Write-Host 'Where should Looppaper keep its files?'
    Write-Host '  1) Standard (default): settings in %APPDATA%\Looppaper, pictures and videos in Pictures\Looppaper,'
    Write-Host '     cache and logs in %LOCALAPPDATA%\Looppaper'
    Write-Host "  2) Everything in one folder: $InstallDir (settings, collection, cache and logs next to Looppaper.exe)"
    $answer = $null
    try {
        $answer = Read-Host 'Choose 1 or 2 [1]'
    } catch {
        # Not interactive (e.g. -NonInteractive): use the default.
    }
    $Layout = if ("$answer".Trim() -eq '2') { 'Portable' } else { 'Standard' }
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

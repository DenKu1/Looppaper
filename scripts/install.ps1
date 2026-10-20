<#
.SYNOPSIS
    Installs Looppaper for the current user (no admin rights needed).

.DESCRIPTION
    Copies Looppaper.exe to %LOCALAPPDATA%\Programs\Looppaper, creates the default collection
    folder (%USERPROFILE%\Pictures\Looppaper), registers Looppaper to start when you sign in,
    and starts it. Run again to upgrade.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    # Path to Looppaper.exe. Defaults to the exe next to this script, or ..\publish\Looppaper.exe in a source checkout.
    [string]$Source
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

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Looppaper'
$exe = Join-Path $installDir 'Looppaper.exe'

if (Test-Path $exe) {
    Write-Host 'Stopping the running Looppaper (if any)...'
    Start-Process -FilePath $exe -ArgumentList '--stop' -Wait -WindowStyle Hidden
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -LiteralPath (Resolve-Path $Source) -Destination $exe -Force
Write-Host "Installed to $exe"

$collection = Join-Path $env:USERPROFILE 'Pictures\Looppaper'
New-Item -ItemType Directory -Force -Path $collection | Out-Null
Write-Host "Collection folder: $collection (put .jpg/.jpeg/.png pictures and .mp3 music here)"

$p = Start-Process -FilePath $exe -ArgumentList '--install-startup' -Wait -PassThru -WindowStyle Hidden
if ($p.ExitCode -ne 0) {
    throw "Registering Looppaper to start at sign-in failed (exit code $($p.ExitCode)). See $env:LOCALAPPDATA\Looppaper\logs\looppaper.log"
}
Write-Host 'Looppaper will start when you sign in.'

Start-Process -FilePath $exe
Write-Host "Looppaper is running. Settings: $env:APPDATA\Looppaper\config.json (restart Looppaper after editing)."

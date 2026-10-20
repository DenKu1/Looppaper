<#
.SYNOPSIS
    Removes Looppaper for the current user.

.DESCRIPTION
    Stops Looppaper (restoring your normal wallpaper), removes the sign-in startup entry and
    deletes %LOCALAPPDATA%\Programs\Looppaper. Your pictures and videos are never touched.
    Settings and logs are kept unless -RemoveSettings is given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -RemoveSettings
#>
[CmdletBinding()]
param(
    # Also delete %APPDATA%\Looppaper (config.json) and %LOCALAPPDATA%\Looppaper (logs).
    [switch]$RemoveSettings
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Looppaper'
$exe = Join-Path $installDir 'Looppaper.exe'

if (Test-Path $exe) {
    Start-Process -FilePath $exe -ArgumentList '--stop' -Wait -WindowStyle Hidden
    Start-Process -FilePath $exe -ArgumentList '--uninstall-startup' -Wait -WindowStyle Hidden
} else {
    # The exe is gone; remove the startup entry directly.
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Looppaper' -ErrorAction SilentlyContinue
}

if (Test-Path $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
    Write-Host "Removed $installDir"
}

if ($RemoveSettings) {
    foreach ($dir in @((Join-Path $env:APPDATA 'Looppaper'), (Join-Path $env:LOCALAPPDATA 'Looppaper'))) {
        if (Test-Path $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Host "Removed $dir"
        }
    }
}

Write-Host 'Looppaper uninstalled. Your pictures and videos were left in place.'

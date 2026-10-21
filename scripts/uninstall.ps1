<#
.SYNOPSIS
    Removes Looppaper for the current user.

.DESCRIPTION
    Stops Looppaper (restoring your normal wallpaper), removes the sign-in startup entry, and deletes
    the program, its cache and its logs. Your pictures and videos are never deleted unless you add
    -RemoveCollection, and your settings are kept unless you add -RemoveSettings.

    Portable layout (the default install): the install folder keeps config.json and collection\
    afterwards; it is removed only if nothing of yours is left in it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -RemoveSettings
#>
[CmdletBinding()]
param(
    # Also delete config.json and the compression records (and, for the standard layout, %APPDATA%\Looppaper).
    [switch]$RemoveSettings,

    # Also delete the collection folder inside the install folder (portable layout), or the Wallpaper Engine
    # videos Looppaper saved in Videos\Looppaper (standard layout). Never touches your own pictures and videos there.
    [switch]$RemoveCollection,

    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Looppaper')
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $InstallDir 'Looppaper.exe'
$portable = Test-Path (Join-Path $InstallDir 'looppaper.portable')

if (Test-Path $exe) {
    Start-Process -FilePath $exe -ArgumentList '--stop' -Wait -WindowStyle Hidden
    Start-Process -FilePath $exe -ArgumentList '--uninstall-startup' -Wait -WindowStyle Hidden
} else {
    # The exe is gone; remove the startup entry directly.
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Looppaper' -ErrorAction SilentlyContinue
}

function Remove-IfPresent([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Host "Removed $Path"
    }
}

# Program files, cache and logs are always removed.
foreach ($name in @('Looppaper.exe', 'looppaper.portable', 'cache', 'logs')) {
    Remove-IfPresent (Join-Path $InstallDir $name)
}

if ($RemoveSettings) {
    Remove-IfPresent (Join-Path $InstallDir 'config.json')
    Remove-IfPresent (Join-Path $InstallDir 'compression.json')
}

if ($RemoveCollection) {
    Remove-IfPresent (Join-Path $InstallDir 'collection')
}

if (-not $portable) {
    # Standard layout: cache and logs live in %LOCALAPPDATA%\Looppaper, settings in %APPDATA%\Looppaper.
    $local = Join-Path $env:LOCALAPPDATA 'Looppaper'
    Remove-IfPresent (Join-Path $local 'cache')
    Remove-IfPresent (Join-Path $local 'logs')
    if ($RemoveCollection) {
        # Only what Looppaper made there: "<title> (we-<id>-<time>).mp4" recordings and copies.
        $videos = Join-Path $env:USERPROFILE 'Videos\Looppaper'
        if (Test-Path -LiteralPath $videos) {
            Get-ChildItem -LiteralPath $videos -File -Filter '*.mp4' |
                Where-Object { $_.BaseName -match '(^|\()we-[A-Za-z0-9_-]+-\d{8}T\d{6}\)?$' } |
                ForEach-Object { Remove-IfPresent $_.FullName }
            if (-not (Get-ChildItem -LiteralPath $videos -Force | Select-Object -First 1)) {
                Remove-IfPresent $videos
            }
        }
    }
    if ($RemoveSettings) {
        Remove-IfPresent $local
        Remove-IfPresent (Join-Path $env:APPDATA 'Looppaper')
    }
}

if ((Test-Path $InstallDir) -and -not (Get-ChildItem -LiteralPath $InstallDir -Force | Select-Object -First 1)) {
    Remove-Item -LiteralPath $InstallDir -Force
    Write-Host "Removed $InstallDir"
} elseif (Test-Path $InstallDir) {
    Write-Host "Kept your files in $InstallDir (settings and/or collection)."
}

if ($RemoveCollection) {
    Write-Host 'Looppaper uninstalled.'
} else {
    Write-Host 'Looppaper uninstalled. Your pictures and videos were left in place.'
}

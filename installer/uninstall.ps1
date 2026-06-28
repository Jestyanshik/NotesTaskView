param(
    [switch]$RemoveUserSettings
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'NotesTaskView'
$installExe = Join-Path $installDir 'NotesTaskView.exe'
$installConfig = Join-Path $installDir 'appsettings.json'
$installIcon = Join-Path $installDir 'app-icon.ico'
$installPdb = Join-Path $installDir 'NotesTaskView.pdb'
$settingsFile = Join-Path $installDir 'settings.json'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\NotesTaskView.lnk'
$startupShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\NotesTaskView.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NotesTaskView.lnk'

foreach ($path in @($startMenuShortcut, $startupShortcut, $desktopShortcut)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

foreach ($path in @($installExe, $installConfig, $installIcon, $installPdb)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

if ($RemoveUserSettings) {
    Get-ChildItem -LiteralPath $installDir -Filter 'settings*.json' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

if ((Test-Path -LiteralPath $installDir) -and
    -not (Get-ChildItem -LiteralPath $installDir -Force -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $installDir -Force
}

Write-Host 'NotesTaskView removed.'
if (-not $RemoveUserSettings -and (Test-Path -LiteralPath $settingsFile)) {
    Write-Host "User settings were kept: $settingsFile"
}

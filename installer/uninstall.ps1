$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'NotesTaskView'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\NotesTaskView.lnk'
$startupShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\NotesTaskView.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NotesTaskView.lnk'

foreach ($path in @($startMenuShortcut, $startupShortcut, $desktopShortcut)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

Write-Host 'NotesTaskView removed.'

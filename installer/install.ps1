param(
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot 'publish\win-x64-self-contained'
$sourceExe = Join-Path $publishDir 'NotesTaskView.exe'
$sourceConfig = Join-Path $publishDir 'appsettings.json'
$sourceIcon = Join-Path $projectRoot 'Assets\app-icon.ico'

$installDir = Join-Path $env:LOCALAPPDATA 'NotesTaskView'
$installExe = Join-Path $installDir 'NotesTaskView.exe'
$installConfig = Join-Path $installDir 'appsettings.json'
$installIcon = Join-Path $installDir 'app-icon.ico'

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Self-contained exe not found. Run dotnet publish first: $sourceExe"
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installExe -Force

if (Test-Path -LiteralPath $sourceConfig) {
    Copy-Item -LiteralPath $sourceConfig -Destination $installConfig -Force
}

if (Test-Path -LiteralPath $sourceIcon) {
    Copy-Item -LiteralPath $sourceIcon -Destination $installIcon -Force
}

$shell = New-Object -ComObject WScript.Shell
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$startMenuShortcut = Join-Path $startMenuDir 'NotesTaskView.lnk'
$startupShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\NotesTaskView.lnk'
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NotesTaskView.lnk'

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $installExe
    $shortcut.WorkingDirectory = $installDir
    if (Test-Path -LiteralPath $installIcon) {
        $shortcut.IconLocation = $installIcon
    }
    $shortcut.Description = 'NotesTaskView overlay notes'
    $shortcut.Save()
}

New-AppShortcut -Path $startMenuShortcut
New-AppShortcut -Path $startupShortcut

if ($DesktopShortcut) {
    New-AppShortcut -Path $desktopShortcutPath
}

Write-Host "NotesTaskView installed to $installDir"
Write-Host 'Self-contained build does not require .NET Runtime on the target machine.'

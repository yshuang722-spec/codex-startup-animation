param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\CodexStartupAnimation")
)

$ErrorActionPreference = "Stop"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Codex 启动.lnk"

if (Test-Path -LiteralPath $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
    Write-Host "Removed desktop shortcut."
}

if (Test-Path -LiteralPath $InstallDirectory) {
    Write-Host "Launcher files remain at: $InstallDirectory"
    Write-Host "Delete that folder manually after confirming you no longer need it."
}

Write-Host "The official Codex application was not changed."

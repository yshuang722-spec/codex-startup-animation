param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\CodexStartupAnimation"),
    [switch]$NoLaunch,
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$buildOutput = Join-Path $repoRoot "dist"
$sourceDirectory = if (Test-Path -LiteralPath (Join-Path $buildOutput "ChatGPT-Boot.exe")) {
    $buildOutput
} else {
    $repoRoot
}
$sourceExecutable = Join-Path $sourceDirectory "ChatGPT-Boot.exe"

if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    $buildScript = Join-Path $repoRoot "build.ps1"
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "ChatGPT-Boot.exe is missing and build.ps1 was not found."
    }
    & $buildScript -OutputDirectory $buildOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
    $sourceDirectory = $buildOutput
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null

$installFiles = @(
    "ChatGPT-Boot.exe"
    "ChatGPT-Green-GPT.ico"
    "logo-mark.path"
    "README.md"
    "DISCLAIMER.md"
    "LICENSE"
)

foreach ($file in $installFiles) {
    $source = Join-Path $sourceDirectory $file
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $InstallDirectory -Force
    }
}

$installedExecutable = Join-Path $InstallDirectory "ChatGPT-Boot.exe"
Write-Host "Installed to: $InstallDirectory"

if (-not $NoShortcut) {
    $installedIcon = Join-Path $InstallDirectory "ChatGPT-Green-GPT.ico"
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Codex 启动.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($desktopShortcut)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.IconLocation = "$installedIcon,0"
    $shortcut.Description = "Codex Engine Boot"
    $shortcut.Save()
    Write-Host "Desktop shortcut: $desktopShortcut"
}

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExecutable
}

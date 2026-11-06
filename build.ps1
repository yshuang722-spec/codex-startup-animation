param(
    [string]$OutputDirectory = "dist",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDirectory = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repoRoot $OutputDirectory
}
$executablePath = Join-Path $outputPath "ChatGPT-Boot.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The .NET Framework C# compiler was not found at: $compiler"
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$arguments = @(
    "/nologo"
    "/target:winexe"
    "/platform:anycpu"
    "/optimize+"
    "/out:$executablePath"
    "/win32icon:$repoRoot\ChatGPT-Green-GPT.ico"
    "/win32manifest:$repoRoot\Boot.manifest"
    "/resource:$repoRoot\logo-mark.path,logo-mark.path"
    "/reference:$wpfDirectory\PresentationFramework.dll"
    "/reference:$wpfDirectory\PresentationCore.dll"
    "/reference:$wpfDirectory\WindowsBase.dll"
    "/reference:System.Core.dll"
    "/reference:System.Drawing.dll"
    "/reference:System.Windows.Forms.dll"
    "/reference:System.Xaml.dll"
    "/reference:System.Xml.dll"
    "$repoRoot\Boot.cs"
)

Write-Host "Building Codex launch animation..."
& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$releaseFiles = @(
    "ChatGPT-Boot.exe"
    "ChatGPT-Green-GPT.ico"
    "logo-mark.path"
    "README.md"
    "DISCLAIMER.md"
    "LICENSE"
    "CHANGELOG.md"
    "Boot.cs"
    "Boot.manifest"
    "build.ps1"
    "install.ps1"
    "uninstall.ps1"
    "startup-animation-v2.html"
    "logo-line-black.png"
    "logo-line-gradient.png"
    "ChatGPT-Green-GPT.png"
    "启动 ChatGPT.cmd"
    "Start-ChatGPT.ps1"
    "Create-Desktop-Shortcut.vbs"
)

foreach ($file in $releaseFiles) {
    $source = Join-Path $repoRoot $file
    if (Test-Path -LiteralPath $source) {
        $destination = Join-Path $outputPath $file
        if (
            [IO.Path]::GetFullPath($source) -ne
            [IO.Path]::GetFullPath($destination)
        ) {
            Copy-Item -LiteralPath $source -Destination $outputPath -Force
        }
    }
}

if ($Zip) {
    $zipPath = Join-Path $repoRoot "Codex-Startup-Animation.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $outputPath "*") -DestinationPath $zipPath
    Write-Host "Created $zipPath"
}

$item = Get-Item -LiteralPath $executablePath
Write-Host ("Built {0} ({1:N0} bytes)" -f $item.FullName, $item.Length)

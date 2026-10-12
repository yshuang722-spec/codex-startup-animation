$ErrorActionPreference = 'Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'ChatGPT-Boot.exe') -WindowStyle Hidden

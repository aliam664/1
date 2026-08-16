# Build StockCorsa as a Windows portable EXE + NSIS installer.
# Run on Windows 10/11 with Node 22+.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
npm install
npm test
npm run catalog:build
npx electron-builder --win portable nsis --x64 --publish never
Write-Host "Output: dist-electron\"

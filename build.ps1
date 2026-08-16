param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish\win-x64'
$installer = Join-Path $root 'artifacts\installer'
Remove-Item $publish,$installer -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore (Join-Path $root 'ACModHub.sln')
dotnet build (Join-Path $root 'ACModHub.sln') -c Release --no-restore
if (-not $SkipTests) { dotnet test (Join-Path $root 'ACModHub.sln') -c Release --no-build }
dotnet publish (Join-Path $root 'src\ACModHub.App\ACModHub.App.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $publish
$portable = Join-Path $root 'artifacts\AC-Mod-Hub-Portable-win-x64.zip'
Compress-Archive -Path "$publish\*" -DestinationPath $portable -Force
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php' }
& $iscc "/DPublishDir=$publish" (Join-Path $root 'installer\ACModHub.iss')
Write-Host "Portable: $portable"
Write-Host "Installer: $(Join-Path $installer 'AC-Mod-Hub-Setup.exe')"

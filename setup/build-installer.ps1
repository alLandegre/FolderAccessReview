$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

$png = Join-Path $root "assets\folder-access-review-icon.png"
$ico = Join-Path $root "assets\FolderAccessReview.ico"
$publishDir = Join-Path $root "publish\installer-src"
$outDir = Join-Path $root "installer"

Write-Host "==> Icon"
dotnet run --project (Join-Path $root "tools\MakeIcon\MakeIcon.csproj") -c Release -- $png $ico

Write-Host "==> Publish self-contained"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root "src\NeFs.AclAuditor\NeFs.AclAuditor.csproj") `
  -c Release -r win-x64 --self-contained true -o $publishDir

Write-Host "==> Inno Setup"
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6." }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $iscc (Join-Path $root "setup\FolderAccessReview.iss")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem $outDir -Filter "*.exe" | Format-Table Name, Length, LastWriteTime -AutoSize
Write-Host "Done."

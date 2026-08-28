$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$DistRoot = Join-Path $Root "dist"
$Dist = Join-Path $DistRoot "gloader"
$Publish = Join-Path $DistRoot "publish"
$Deps = Join-Path $Dist "gdeps"
$ModsOut = Join-Path $Dist "gmods"
$Mods = Join-Path $Root "gmods"

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

New-Item $Dist -ItemType Directory -Force | Out-Null
New-Item $Deps -ItemType Directory -Force | Out-Null
New-Item $ModsOut -ItemType Directory -Force | Out-Null

dotnet publish $Project -c Release -o $Publish

Move-Item (Join-Path $Publish "gloader.exe") (Join-Path $Dist "gloader.exe") -Force
Get-ChildItem $Publish -Force | Move-Item -Destination $Deps -Force
Copy-Item (Join-Path $Mods "*") $ModsOut -Recurse -Force
Copy-Item (Join-Path $Root "LICENSE.md") (Join-Path $Deps "LICENSE.md") -Force
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.txt") (Join-Path $Deps "THIRD-PARTY-NOTICES.txt") -Force
Remove-Item $Publish -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "gloader.exe lives in the game root, mods live under gmods, and loader dependencies live under gdeps."

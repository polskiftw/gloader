$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$DistRoot = Join-Path $Root "dist"
$Dist = Join-Path $DistRoot "gloader"
$Publish = Join-Path $DistRoot "publish"
$Runtime = Join-Path $Dist "gmods"
$Mods = Join-Path $Root "gmods"

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

New-Item $Dist -ItemType Directory -Force | Out-Null
New-Item $Runtime -ItemType Directory -Force | Out-Null

dotnet publish $Project -c Release -o $Publish

Move-Item (Join-Path $Publish "gloader.exe") (Join-Path $Dist "gloader.exe") -Force
Get-ChildItem $Publish -Force | Move-Item -Destination $Runtime -Force
Copy-Item (Join-Path $Mods "*") $Runtime -Recurse -Force
Remove-Item $Publish -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "Only gloader.exe is added to the game root; every other gloader file lives under gmods."

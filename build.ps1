$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$DistRoot = Join-Path $Root "dist"
$Dist = Join-Path $DistRoot "gloader"
$Publish = Join-Path $DistRoot "publish"
$Deps = Join-Path $Dist "gdeps"
$ModsOut = Join-Path $Dist "gmods"
$Mods = Join-Path $Root "gmods"

function Enable-LargeAddressAware([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a valid PE executable: $Path"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length) {
        throw "Invalid PE header offset in $Path"
    }

    if ($bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or
        $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
        throw "Invalid PE signature in $Path"
    }

    # IMAGE_FILE_HEADER.Characteristics is 18 bytes after the PE signature.
    $characteristicsOffset = $peOffset + 22
    $characteristics = [BitConverter]::ToUInt16($bytes, $characteristicsOffset)
    $characteristics = [uint16]($characteristics -bor 0x20) # IMAGE_FILE_LARGE_ADDRESS_AWARE
    [BitConverter]::GetBytes($characteristics).CopyTo($bytes, $characteristicsOffset)
    [IO.File]::WriteAllBytes($Path, $bytes)

    $verify = [IO.File]::ReadAllBytes($Path)
    $verifyValue = [BitConverter]::ToUInt16($verify, $characteristicsOffset)
    if (($verifyValue -band 0x20) -eq 0) {
        throw "Failed to enable LARGEADDRESSAWARE on $Path"
    }

    Write-Host ("Large Address Aware: {0} (Characteristics=0x{1:X4})" -f $Path, $verifyValue)
}

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

New-Item $Dist -ItemType Directory -Force | Out-Null
New-Item $Deps -ItemType Directory -Force | Out-Null
New-Item $ModsOut -ItemType Directory -Force | Out-Null

dotnet publish $Project -c Release -o $Publish

$PublishedExe = Join-Path $Publish "gloader.exe"
$DistExe = Join-Path $Dist "gloader.exe"
Move-Item $PublishedExe $DistExe -Force

# THICC has already passed 16,800x4,800 generation/save/reload with an x86 LAA
# gloader host. The distributed launcher is the process that hosts Terraria, so
# make that proven address-space requirement part of the normal package instead
# of leaving it as a special CI-only probe mutation.
Enable-LargeAddressAware $DistExe

Get-ChildItem $Publish -Force | Move-Item -Destination $Deps -Force
Copy-Item (Join-Path $Mods "*") $ModsOut -Recurse -Force
Copy-Item (Join-Path $Root "LICENSE.md") (Join-Path $Deps "LICENSE.md") -Force
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.txt") (Join-Path $Deps "THIRD-PARTY-NOTICES.txt") -Force
Remove-Item $Publish -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "gloader.exe lives in the game root, mods live under gmods, and loader dependencies live under gdeps."
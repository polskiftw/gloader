$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$CompilerProject = Join-Path $Root "src\GLoader.Compiler\GLoader.Compiler.csproj"
$WorldMakerProject = Join-Path $Root "tools\expanded-world-maker\ExpandedWorldMaker.csproj"
$CoreMods = Join-Path $Root "src\GLoader.CoreMods"
$DistRoot = Join-Path $Root "dist"
$Dist = Join-Path $DistRoot "gloader"
$Publish = Join-Path $DistRoot "publish"
$CompilerPublish = Join-Path $DistRoot "compiler-publish"
$WorldMakerPublish = Join-Path $DistRoot "world-maker-publish"
$Deps = Join-Path $Dist "gdeps"
$CompilerOut = Join-Path $Deps "compiler"
$CoreModsOut = Join-Path $Deps "coremods"
$ModsOut = Join-Path $Dist "gmods"
$ToolsOut = Join-Path $Dist "tools"
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
New-Item $CompilerOut -ItemType Directory -Force | Out-Null
New-Item $CoreModsOut -ItemType Directory -Force | Out-Null
New-Item $ModsOut -ItemType Directory -Force | Out-Null
New-Item $ToolsOut -ItemType Directory -Force | Out-Null

# Main loader: deliberately no Roslyn package references. It hosts Terraria and
# Harmony, but source compilation happens in the short-lived helper below.
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
Remove-Item $Publish -Recurse -Force

# Roslyn lives in its own helper process under gdeps\compiler. This avoids the
# malware-like combination of an in-process arbitrary C# compiler plus runtime
# patching in gloader.exe. The helper exits before Terraria starts.
dotnet publish $CompilerProject -c Release -o $CompilerPublish
$CompilerExe = Join-Path $CompilerPublish "gloader.compiler.exe"
if (-not (Test-Path $CompilerExe)) {
    throw "gloader.compiler.exe was not produced."
}
Get-ChildItem $CompilerPublish -Force | Move-Item -Destination $CompilerOut -Force
Remove-Item $CompilerPublish -Recurse -Force

# Host & Play's Process.Start/Reflection.Emit redirect is also kept out of the
# distributed gloader.exe. Ship it as raw C# and compile it only after launch.
if (-not (Test-Path (Join-Path $CoreMods "HostPlay\Main.cs"))) {
    throw "Built-in Host Play source mod is missing."
}
Copy-Item (Join-Path $CoreMods "*") $CoreModsOut -Recurse -Force

# Keep the separation enforceable: Roslyn must never leak back into the main
# gdeps probing directory. It belongs only beside the short-lived compiler helper.
$roslynInMainDeps = @(Get-ChildItem $Deps -File -Filter "Microsoft.CodeAnalysis*.dll" -ErrorAction SilentlyContinue)
if ($roslynInMainDeps.Count -ne 0) {
    throw "Roslyn leaked into main gdeps: $($roslynInMainDeps.Name -join ', ')"
}
if (-not (Test-Path (Join-Path $CompilerOut "gloader.compiler.exe"))) {
    throw "Packaged compiler helper is missing from gdeps\\compiler."
}
if (-not (Test-Path (Join-Path $CoreModsOut "HostPlay\Main.cs"))) {
    throw "Packaged Host Play core source is missing from gdeps\\coremods."
}

Copy-Item (Join-Path $Mods "*") $ModsOut -Recurse -Force
Copy-Item (Join-Path $Root "LICENSE.md") (Join-Path $Deps "LICENSE.md") -Force
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.txt") (Join-Path $Deps "THIRD-PARTY-NOTICES.txt") -Force

# Expanded World Maker is part of the normal package, not a post-release overlay.
# This prevents a later gloader refresh from accidentally publishing a ZIP that
# drops the GUI world generator.
dotnet publish $WorldMakerProject -c Release -o $WorldMakerPublish
$WorldMakerExe = Join-Path $WorldMakerPublish "ExpandedWorldMaker.exe"
if (-not (Test-Path $WorldMakerExe)) {
    throw "ExpandedWorldMaker.exe was not produced."
}
Copy-Item $WorldMakerExe (Join-Path $ToolsOut "ExpandedWorldMaker.exe") -Force
Copy-Item (Join-Path $Root "tools\expanded-world-maker\README.md") (Join-Path $ToolsOut "ExpandedWorldMaker.README.md") -Force
Copy-Item (Join-Path $Root "tools\expanded-world-maker\DGD.md") (Join-Path $ToolsOut "ExpandedWorldMaker.DGD.md") -Force
Remove-Item $WorldMakerPublish -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "gloader.exe lives in the game root, mods live under gmods, dependencies under gdeps, and tools under tools."

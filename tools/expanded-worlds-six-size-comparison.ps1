$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo
$out = Join-Path $repo 'comparison'
New-Item -ItemType Directory -Force $out | Out-Null

$runId = 0L
if (-not [long]::TryParse($env:GITHUB_RUN_ID, [ref]$runId)) {
    $runId = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
}
$seed = [int](100000000 + ($runId % 1900000000))
$seed | Set-Content (Join-Path $out 'seed.txt') -Encoding ascii
Write-Host "Fresh comparison seed: $seed"

function Add-Summary([string]$line) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        Add-Content $env:GITHUB_STEP_SUMMARY $line
    }
}

Add-Summary '### Expanded Worlds real six-size comparison'
Add-Summary "- Same fresh seed for all six worlds: **$seed**"

# Official Terraria 1.4.5.8 dedicated server.
$serverZip = Join-Path $env:RUNNER_TEMP 'terraria-server-1458.zip'
$serverRoot = Join-Path $env:RUNNER_TEMP 'terraria-server-1458'
Invoke-WebRequest 'https://terraria.org/api/download/pc-dedicated-server/terraria-server-1458.zip' -OutFile $serverZip
Expand-Archive $serverZip $serverRoot -Force
$server = Join-Path $serverRoot '1458/Windows/TerrariaServer.exe'
if (-not (Test-Path $server)) { throw "Official TerrariaServer.exe missing: $server" }
$serverDir = Split-Path -Parent $server
Write-Host "TerrariaServer SHA256: $((Get-FileHash $server -Algorithm SHA256).Hash)"

# Build the actual x86 loader and enable Large Address Aware, matching the successful
# 16,800 x 4,800 proof run.
dotnet build src/GLoader/GLoader.csproj -c Release -p:PlatformTarget=x86 -p:Prefer32Bit=true
$gloader = Join-Path $repo 'src/GLoader/bin/Release/net48/gloader.exe'
if (-not (Test-Path $gloader)) { throw "gloader.exe missing: $gloader" }
$bytes = [IO.File]::ReadAllBytes($gloader)
$pe = [BitConverter]::ToInt32($bytes, 0x3c)
$charOffset = $pe + 22
$characteristics = [BitConverter]::ToUInt16($bytes, $charOffset)
$characteristics = [uint16]($characteristics -bor 0x20)
[BitConverter]::GetBytes($characteristics).CopyTo($bytes, $charOffset)
[IO.File]::WriteAllBytes($gloader, $bytes)
$verify = [IO.File]::ReadAllBytes($gloader)
$verifyPe = [BitConverter]::ToInt32($verify, 0x3c)
$verifyCharacteristics = [BitConverter]::ToUInt16($verify, $verifyPe + 22)
if (($verifyCharacteristics -band 0x20) -eq 0) { throw 'Failed to enable Large Address Aware on gloader.exe.' }

# Normal Expanded Worlds staging for XL/Huge.
$normalMods = Join-Path $env:RUNNER_TEMP 'ew-comparison-normal-gmods'
New-Item -ItemType Directory -Force $normalMods | Out-Null
Copy-Item (Join-Path $repo 'gmods/ExpandedWorlds') (Join-Path $normalMods 'ExpandedWorlds') -Recurse -Force

# Isolated THICC staging. This preserves normal Huge at 16,800 x 2,400 while using
# the already-proven 16,800 x 4,800 runtime configuration for the sixth comparison world.
$thiccMods = Join-Path $env:RUNNER_TEMP 'ew-comparison-thicc-gmods'
$thiccMod = Join-Path $thiccMods 'ExpandedWorlds'
New-Item -ItemType Directory -Force $thiccMods | Out-Null
Copy-Item (Join-Path $repo 'gmods/ExpandedWorlds') $thiccMod -Recurse -Force

function Replace-Once([string]$path, [string]$old, [string]$new) {
    $text = Get-Content -Raw $path
    $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
    if ($count -ne 1) { throw "Expected one '$old' in $path; found $count." }
    Set-Content $path ($text.Replace($old, $new)) -Encoding UTF8
}

Replace-Once (Join-Path $thiccMod 'GenerationMath.cs') 'public const int HugeHeight = 2400;' 'public const int HugeHeight = 4800;'
Replace-Once (Join-Path $thiccMod 'ServerRuntime.cs') 'internal const int VanillaLargeHeight = 2400;' 'internal const int VanillaLargeHeight = 4800;'
$storage = Join-Path $thiccMod 'WorldStorage.cs'
$storageText = Get-Content -Raw $storage
$guardPattern = 'return\s+height\s*==\s*ExpandedWorldMath\.LargeHeight\s*&&\s*\(width\s*==\s*ExpandedWorldMath\.XLWidth\s*\|\|\s*width\s*==\s*ExpandedWorldMath\.HugeWidth\s*\);'
if ([regex]::Matches($storageText, $guardPattern).Count -ne 1) { throw 'WorldStorage dimension guard shape changed.' }
$guardReplacement = 'return (width == ExpandedWorldMath.XLWidth && height == ExpandedWorldMath.XLHeight) ||' + [Environment]::NewLine + '               (width == ExpandedWorldMath.HugeWidth && height == ExpandedWorldMath.HugeHeight);'
Set-Content $storage ([regex]::Replace($storageText, $guardPattern, $guardReplacement, 1)) -Encoding UTF8

$worldRoot = Join-Path $env:RUNNER_TEMP 'ew-comparison-worlds'
New-Item -ItemType Directory -Force $worldRoot | Out-Null

function Test-Port([int]$port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync('127.0.0.1', $port)
        if (-not $task.Wait(300)) { return $false }
        return $client.Connected
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Generate-World(
    [string]$name,
    [int]$autocreate,
    [int]$width,
    [int]$height,
    [int]$port,
    [string]$expanded,
    [string]$mods
) {
    $saveRoot = Join-Path $worldRoot $name
    $worldDir = Join-Path $saveRoot 'Worlds'
    New-Item -ItemType Directory -Force $worldDir | Out-Null
    $world = Join-Path $worldDir ($name + '.wld')
    $stdout = Join-Path $out ($name + '-stdout.txt')
    $stderr = Join-Path $out ($name + '-stderr.txt')

    $game = @(
        '-savedirectory', ('"' + $saveRoot + '"'),
        '-world', ('"' + $world + '"'),
        '-autocreate', $autocreate,
        '-worldname', ('EW-' + $name),
        '-seed', $seed,
        '-difficulty', '0',
        '-port', $port,
        '-maxplayers', '1',
        '-noupnp'
    )

    if ([string]::IsNullOrWhiteSpace($expanded)) {
        $exe = $server
        $argString = $game -join ' '
    }
    else {
        $exe = $gloader
        $env:GLOADER_EXPANDED_WORLD = $expanded
        $loaderArgs = @('--target', ('"' + $server + '"'), '--mods', ('"' + $mods + '"'), '--')
        $argString = ($loaderArgs + $game) -join ' '
    }

    Write-Host "=== Generate ${name}: $width x $height, seed $seed ==="
    $process = Start-Process -FilePath $exe -ArgumentList $argString -WorkingDirectory $serverDir -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $ready = $false
    $deadline = (Get-Date).AddMinutes(100)

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { break }
        if (Test-Path $stdout) {
            $live = Get-Content -Raw $stdout -ErrorAction SilentlyContinue
            if ($live -and ($live.Contains('Mod failed: ExpandedWorlds') -or $live.Contains('Dedicated-server generation failed:'))) {
                break
            }
        }
        if (Test-Port $port) {
            $ready = $true
            Start-Sleep -Seconds 2
            break
        }
        Start-Sleep -Seconds 2
    }

    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    try { $process.WaitForExit(10000) | Out-Null } catch {}
    Remove-Item Env:GLOADER_EXPANDED_WORLD -ErrorAction SilentlyContinue

    if (-not $ready) {
        if (Test-Path $stdout) { Get-Content -Raw $stdout | Write-Host }
        if (Test-Path $stderr) { Get-Content -Raw $stderr | Write-Host }
        throw "$name generation never reached server-ready state."
    }
    if (-not (Test-Path $world)) { throw "$name .wld is missing." }
    $info = Get-Item $world
    if ($info.Length -lt 512KB) { throw "$name .wld is unexpectedly small: $($info.Length) bytes." }

    $dest = Join-Path $out ($name + '.wld')
    Copy-Item $world $dest -Force
    $hash = (Get-FileHash $dest -Algorithm SHA256).Hash
    Add-Content (Join-Path $out 'manifest.txt') "$name|$width|$height|$seed|$($info.Length)|$hash"
    Write-Host "PASS ${name}: $width x $height; $($info.Length) bytes; SHA256 $hash"
}

Generate-World 'Small' 1 4200 1200 7841 '' ''
Generate-World 'Medium' 2 6400 1800 7842 '' ''
Generate-World 'Large' 3 8400 2400 7843 '' ''
Generate-World 'XL' 3 12600 2400 7844 'XL' $normalMods
Generate-World 'Huge' 3 16800 2400 7845 'HUGE' $normalMods
Generate-World 'THICC' 3 16800 4800 7846 'HUGE' $thiccMods

# Build a tiny headless front-end against a pinned TEdit source revision. The actual
# world loading and minimap rendering are TEdit's World.LoadWorld and RenderMiniMap.
$teditCommit = 'cbf7b3876e408cf45240b5d792e0d8e57ba78e3f'
$tedit = Join-Path $env:RUNNER_TEMP 'TEdit-pinned'
git clone --filter=blob:none --no-checkout https://github.com/TEdit/Terraria-Map-Editor.git $tedit
git -C $tedit fetch --depth 1 origin $teditCommit
git -C $tedit checkout $teditCommit

$renderer = Join-Path $env:RUNNER_TEMP 'TEditHeadlessRenderer'
New-Item -ItemType Directory -Force $renderer | Out-Null
$teditProj = (Join-Path $tedit 'src/TEdit/TEdit.csproj').Replace('\', '/')
$rendererProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$teditProj" />
  </ItemGroup>
</Project>
"@
Set-Content (Join-Path $renderer 'Renderer.csproj') $rendererProject -Encoding UTF8

$rendererSource = @'
using System;
using System.IO;
using System.Windows.Media.Imaging;
using TEdit.Render;
using TEdit.Terraria;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Usage: renderer world.wld output.png");

        WorldConfiguration.Initialize();
        var (world, error) = World.LoadWorld(args[0]);
        if (error != null)
            throw new Exception("TEdit failed to load the world.", error);
        if (world == null)
            throw new InvalidOperationException("TEdit returned a null world.");

        // One output pixel per eight Terraria tiles for every panel.
        int targetWidth = world.TilesWide / 8;
        int targetHeight = world.TilesHigh / 8;
        var bitmap = RenderMiniMap.Render(world, useFilter: false, showBackground: true,
            targetWidth: targetWidth, targetHeight: targetHeight);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(args[1]);
        encoder.Save(stream);
        Console.WriteLine($"TEdit: {world.Title} {world.TilesWide}x{world.TilesHigh} -> {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        return 0;
    }
}
'@
Set-Content (Join-Path $renderer 'Program.cs') $rendererSource -Encoding UTF8

dotnet build (Join-Path $renderer 'Renderer.csproj') -c Release
$rendererDll = Join-Path $renderer 'bin/Release/net10.0-windows10.0.19041.0/Renderer.dll'
if (-not (Test-Path $rendererDll)) { throw "Headless TEdit renderer missing: $rendererDll" }

$names = @('Small','Medium','Large','XL','Huge','THICC')
foreach ($name in $names) {
    $wld = Join-Path $out ($name + '.wld')
    $png = Join-Path $out ($name + '-map.png')
    Write-Host "=== TEdit render $name ==="
    dotnet $rendererDll $wld $png
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $png)) { throw "TEdit render failed for $name." }
}

# Verify TEdit rendered the real physical dimensions at one common scale.
Add-Type -AssemblyName System.Drawing
$expected = @{
    Small  = '525x150'
    Medium = '800x225'
    Large  = '1050x300'
    XL     = '1575x300'
    Huge   = '2100x300'
    THICC  = '2100x600'
}
foreach ($name in $names) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $out ($name + '-map.png')))
    try { $actual = "$($image.Width)x$($image.Height)" }
    finally { $image.Dispose() }
    if ($actual -ne $expected[$name]) { throw "$name TEdit map expected $($expected[$name]), got $actual." }
    Write-Host "PASS TEdit ${name}: $actual"
}

# Mechanically compose the six untouched TEdit PNGs. No generative-image tooling is
# involved anywhere in this workflow.
$rows = @(
    @{ Name='Small';  Label='1. Small — 4200 × 1200' },
    @{ Name='Medium'; Label='2. Medium — 6400 × 1800' },
    @{ Name='Large';  Label='3. Large — 8400 × 2400' },
    @{ Name='XL';     Label='4. XL — 12600 × 2400' },
    @{ Name='Huge';   Label='5. Huge — 16800 × 2400' },
    @{ Name='THICC';  Label='6. THICC — 16800 × 4800' }
)
$images = @{}
foreach ($row in $rows) {
    $images[$row.Name] = [System.Drawing.Image]::FromFile((Join-Path $out ($row.Name + '-map.png')))
}

$canvasWidth = 2300
$top = 132
$labelHeight = 40
$gap = 30
$bottom = 50
$canvasHeight = $top + $bottom
foreach ($row in $rows) { $canvasHeight += $labelHeight + $images[$row.Name].Height + $gap }

$bitmap = New-Object System.Drawing.Bitmap($canvasWidth, $canvasHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::FromArgb(16,22,31))
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$titleFont = New-Object System.Drawing.Font('Segoe UI', 34, [System.Drawing.FontStyle]::Bold)
$subtitleFont = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Regular)
$labelFont = New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Bold)
$white = [System.Drawing.Brushes]::White
$blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(105,175,255))
$border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210,220,230), 2)
$center = New-Object System.Drawing.StringFormat
$center.Alignment = [System.Drawing.StringAlignment]::Center

$graphics.DrawString('Expanded Worlds — Same Seed, All Sizes', $titleFont, $white, [System.Drawing.RectangleF]::new(0,20,$canvasWidth,55), $center)
$graphics.DrawString("Real Terraria 1.4.5.8 worldgen • TEdit render • seed $seed", $subtitleFont, $blue, [System.Drawing.RectangleF]::new(0,80,$canvasWidth,35), $center)
$y = $top
foreach ($row in $rows) {
    $image = $images[$row.Name]
    $graphics.DrawString($row.Label, $labelFont, $white, [System.Drawing.RectangleF]::new(0,$y,$canvasWidth,$labelHeight), $center)
    $y += $labelHeight
    $x = [int](($canvasWidth - $image.Width) / 2)
    $graphics.DrawImageUnscaled($image, $x, $y)
    $graphics.DrawRectangle($border, $x, $y, $image.Width - 1, $image.Height - 1)
    $y += $image.Height + $gap
}

$final = Join-Path $out 'ExpandedWorlds-SameSeed-AllSizes-TEdit.png'
$bitmap.Save($final, [System.Drawing.Imaging.ImageFormat]::Png)
foreach ($image in $images.Values) { $image.Dispose() }
$border.Dispose()
$blue.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$labelFont.Dispose()
$center.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

# Keep the artifact image/log focused. The runner used the six real .wld files, but
# they are large and are not needed after TEdit has rendered them.
Remove-Item (Join-Path $out '*.wld') -Force

Add-Summary '- World generation: **PASS — six real .wld files from one seed**'
Add-Summary '- Renderer: **PASS — pinned TEdit World.LoadWorld + RenderMiniMap for all six worlds**'
Add-Summary '- Scale: **1 output pixel = 8 Terraria tiles for every panel**'
Add-Summary '- Final comparison: **ExpandedWorlds-SameSeed-AllSizes-TEdit.png**'
Write-Host "FINAL: $final"

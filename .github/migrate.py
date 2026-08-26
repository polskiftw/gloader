from pathlib import Path


def replace_exact(path, old, new):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Expected text not found in {path}: {old!r}")
    p.write_text(text.replace(old, new), encoding="utf-8")


# Terraria root: gloader.exe only. gmods is the complete GLoader support directory.
replace_exact(
    "src/GLoader/Program.cs",
    '            var loaderDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);\n            var options = LoaderOptions.Parse(args);',
    '            var loaderDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);\n            var supportDirectory = Path.Combine(loaderDirectory, "gmods");\n            var startupResolver = new ManagedAssemblyResolver(supportDirectory);\n            var options = LoaderOptions.Parse(args);')
replace_exact(
    "src/GLoader/Program.cs",
    '                var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)\n                    ? Path.Combine(loaderDirectory, "gmods")\n                    : Path.GetFullPath(options.ModsPath);',
    '                var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)\n                    ? supportDirectory\n                    : Path.GetFullPath(options.ModsPath);')
replace_exact(
    "src/GLoader/Program.cs",
    '                    Path.Combine(loaderDirectory, "logs"),',
    '                    Path.Combine(supportDirectory, "logs"),')
replace_exact(
    "src/GLoader/Program.cs",
    '                using (var resolver = new ManagedAssemblyResolver(gameDirectory, loaderDirectory))',
    '                using (var resolver = new ManagedAssemblyResolver(gameDirectory, supportDirectory, modsDirectory))')
replace_exact(
    "src/GLoader/Program.cs",
    '                            loaderDirectory,\n                            isServerTarget);',
    '                            supportDirectory,\n                            isServerTarget);')
replace_exact(
    "src/GLoader/Program.cs",
    '                Console.Error.WriteLine("See logs\\\\gloader-client.log or logs\\\\gloader-server.log for details.");',
    '                Console.Error.WriteLine("See gmods\\\\logs\\\\gloader-client.log or gmods\\\\logs\\\\gloader-server.log for details.");')
replace_exact(
    "src/GLoader/Program.cs",
    '                Log.Dispose();\n            }',
    '                Log.Dispose();\n                startupResolver.Dispose();\n            }')

replace_exact("src/GLoader/ModRuntime.cs", "            string loaderDirectory,", "            string supportDirectory,")
replace_exact("src/GLoader/ModRuntime.cs", "                loaderDirectory);", "                supportDirectory);")
replace_exact("src/GLoader/ReferenceCollector.cs", "            string loaderDirectory)", "            string supportDirectory)")
replace_exact(
    "src/GLoader/ReferenceCollector.cs",
    "            AddManagedFiles(paths, loaderDirectory, overwrite: false);",
    "            AddManagedFiles(paths, supportDirectory, overwrite: false);")

p = Path("src/GLoader/ModDiscovery.cs")
text = p.read_text(encoding="utf-8")
warning_block = '''            foreach (var file in Directory
                .EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Log.Warn(
                    "Ignoring loose file in gmods root: " + Path.GetFileName(file) +
                    ". Each mod must live in its own immediate subfolder.");
            }

'''
if warning_block not in text:
    raise SystemExit("Expected loose-file warning block not found in ModDiscovery.cs")
p.write_text(text.replace(warning_block, ""), encoding="utf-8")

replace_exact("gmods/DVDLogo/Main.cs", '            "Mods",\n            "DVDLogo");', '            "gmods",\n            "DVDLogo");')
replace_exact("gmods/VGMRadio/Settings.cs", '                "Mods",\n                "VGMRadio",', '                "gmods",\n                "VGMRadio",')

replace_exact(
    "src/GLoader/GLoader.csproj",
    '    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>\n    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>',
    '    <AutoGenerateBindingRedirects>false</AutoGenerateBindingRedirects>\n    <GenerateBindingRedirectsOutputType>false</GenerateBindingRedirectsOutputType>')

Path("build.ps1").write_text(r'''$ErrorActionPreference = "Stop"

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
''', encoding="utf-8")

readme = Path("README.md").read_text(encoding="utf-8")
readme = readme.replace(
    '''  gloader.exe
  gloader.exe.config
  gmods/
    InfiniteAngler/''',
    '''  gloader.exe
  gmods/
    [gloader runtime/support files]
    logs/
    InfiniteAngler/''')
old_contract = '''`gmods/` contains **folders only**. Every immediate subfolder is one mod.

```text
gmods/
  InfiniteAngler/
    Main.cs
  NoLiquidDupe/
    Main.cs
  VGMRadio/
    Main.cs
    NowPlaying.cs
    Providers.cs
    Settings.cs
    VGMRadio.ini
  DVDLogo/
    Main.cs
    DVDLogo.ini
    dvd-logo.png
```

There are no loose mod source, config, asset, or documentation files in the `gmods/` root. gloader only discovers mods from immediate subfolders; loose files are ignored and logged as a warning. The build script also refuses to package a `gmods/` directory containing loose files.

Everything belonging to a mod stays inside that mod's folder: `.cs` source, `.ini`/other configuration, images, data files, and any mod-specific documentation. All `.cs` files beneath one mod folder are compiled together as one in-memory assembly.
'''
new_contract = '''`gmods/` is GLoader's **entire support directory**. The Terraria root gets only `gloader.exe`; dependency DLLs, generated support files, and logs all live under `gmods/`.

Mod folders live there too. An immediate subfolder containing enabled `.cs` files is treated as one mod; loose support files in the `gmods/` root are normal and are not treated as mods.

```text
gmods/
  [gloader dependency/support files]
  logs/
  InfiniteAngler/
    Main.cs
  NoLiquidDupe/
    Main.cs
  VGMRadio/
    Main.cs
    NowPlaying.cs
    Providers.cs
    Settings.cs
    VGMRadio.ini
  DVDLogo/
    Main.cs
    DVDLogo.ini
    dvd-logo.png
```

Everything belonging specifically to a mod stays inside that mod's folder: `.cs` source, `.ini`/other configuration, images, data files, and any mod-specific documentation. All `.cs` files beneath one mod folder are compiled together as one in-memory assembly.
'''
if old_contract not in readme:
    raise SystemExit("Expected README gmods contract not found")
readme = readme.replace(old_contract, new_contract)
readme = readme.replace("logs/gloader-client.log\nlogs/gloader-server.log", "gmods/logs/gloader-client.log\ngmods/logs/gloader-server.log")
readme = readme.replace(
    "Copy the **contents** of `dist/gloader/` directly into the Terraria installation folder. Do not put them inside a nested `gloader` directory.",
    "Copy the **contents** of `dist/gloader/` directly into the Terraria installation folder. The package adds only `gloader.exe` to the game root; all other GLoader files are already contained inside `gmods/`.")
Path("README.md").write_text(readme, encoding="utf-8")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ToolRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ToolRoot)
$Project = Join-Path $ToolRoot "src\Gelatin.App\Gelatin.App.csproj"
$ProjectDirectory = Split-Path -Parent $Project
$DistRoot = Join-Path $ToolRoot "dist"
$PublishDirectory = Join-Path $DistRoot "gelatin"
$LegalDirectory = Join-Path $PublishDirectory "licenses"
[xml]$VersionDocument = Get-Content (Join-Path $ToolRoot "Directory.Build.props")
$Version = [string]$VersionDocument.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Could not resolve Gelatin version from Directory.Build.props." }
$Archive = Join-Path $DistRoot ("gelatin-" + $Version + "-win-x64.zip")
$License = Join-Path $RepoRoot "LICENSE.md"
$ThirdPartyNotices = Join-Path $RepoRoot "THIRD-PARTY-NOTICES.txt"

$env:AVALONIA_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Find-NoticeFile {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Roots,
        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root -PathType Container)) {
            continue
        }

        $direct = Join-Path $root $FileName
        if (Test-Path $direct -PathType Leaf) {
            return (Resolve-Path $direct).Path
        }

        $match = Get-ChildItem $root -Recurse -File -Filter $FileName -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    return $null
}

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

New-Item $PublishDirectory -ItemType Directory -Force | Out-Null

dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $PublishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "Gelatin publish failed with exit code $LASTEXITCODE."
}

$Executable = Join-Path $PublishDirectory "Gelatin.exe"
if (-not (Test-Path $Executable -PathType Leaf)) {
    throw "Publish completed without the required Gelatin.exe."
}

# SkiaSharp/HarfBuzz can contribute native PDBs even when project debug symbols are
# disabled. They are optional debugging artifacts and are not required at runtime.
Get-ChildItem $PublishDirectory -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force

# Single-file publish bundles Gelatin.deps.json into Gelatin.exe, so resolve the
# freshly generated intermediate copy to identify the exact shipped dependency versions.
$DepsJsonPath = Get-ChildItem (Join-Path $ProjectDirectory "obj") -Recurse -File -Filter "Gelatin.deps.json" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($DepsJsonPath)) {
    throw "Could not locate the generated Gelatin.deps.json; cannot resolve upstream notices."
}

$DepsJson = Get-Content $DepsJsonPath -Raw | ConvertFrom-Json
$LibraryNames = @($DepsJson.libraries.PSObject.Properties.Name)
$SkiaNativeLibrary = $LibraryNames |
    Where-Object { $_ -like "SkiaSharp.NativeAssets.Win32/*" } |
    Select-Object -First 1
$RuntimeLibrary = $LibraryNames |
    Where-Object { $_ -like "*Microsoft.NETCore.App.Runtime.win-x64/*" } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($SkiaNativeLibrary)) {
    throw "Could not resolve SkiaSharp.NativeAssets.Win32 from Gelatin.deps.json."
}
if ([string]::IsNullOrWhiteSpace($RuntimeLibrary)) {
    throw "Could not resolve the Windows x64 .NET runtime pack from Gelatin.deps.json."
}

$SkiaVersion = ($SkiaNativeLibrary -split '/', 2)[1]
$RuntimeVersion = ($RuntimeLibrary -split '/', 2)[1]
$NugetRootLine = (& dotnet nuget locals global-packages --list | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($NugetRootLine)) {
    throw "Could not determine the NuGet global-packages directory."
}
$NugetRoot = ($NugetRootLine -replace '^\s*global-packages:\s*', '').Trim()
$DotnetRoot = Split-Path -Parent (Get-Command dotnet).Source

$SkiaPackageRoot = Join-Path $NugetRoot ("skiasharp.nativeassets.win32\" + $SkiaVersion)
$SkiaUpstreamNotices = Find-NoticeFile -Roots @($SkiaPackageRoot) -FileName "THIRD-PARTY-NOTICES.txt"
if ([string]::IsNullOrWhiteSpace($SkiaUpstreamNotices)) {
    throw "Could not locate the upstream SkiaSharp.NativeAssets.Win32 third-party notices for version $SkiaVersion."
}

$RuntimeNugetRoot = Join-Path $NugetRoot ("microsoft.netcore.app.runtime.win-x64\" + $RuntimeVersion)
$RuntimePackNugetRoot = Join-Path $NugetRoot ("runtimepack.microsoft.netcore.app.runtime.win-x64\" + $RuntimeVersion)
$RuntimeDotnetPackRoot = Join-Path $DotnetRoot ("packs\Microsoft.NETCore.App.Runtime.win-x64\" + $RuntimeVersion)
$DotnetUpstreamNotices = Find-NoticeFile `
    -Roots @($RuntimeNugetRoot, $RuntimePackNugetRoot, $RuntimeDotnetPackRoot) `
    -FileName "THIRD-PARTY-NOTICES.TXT"
if ([string]::IsNullOrWhiteSpace($DotnetUpstreamNotices)) {
    throw "Could not locate the upstream .NET Runtime third-party notices for version $RuntimeVersion."
}

# Keep the extracted package human-sized: one executable at the root, legal
# paperwork in one obvious folder, and no loose framework/runtime files.
New-Item $LegalDirectory -ItemType Directory -Force | Out-Null
Copy-Item $License (Join-Path $LegalDirectory "LICENSE.md") -Force
Copy-Item $ThirdPartyNotices (Join-Path $LegalDirectory "THIRD-PARTY-NOTICES.txt") -Force
Copy-Item $SkiaUpstreamNotices (Join-Path $LegalDirectory "SKIASHARP-THIRD-PARTY-NOTICES.txt") -Force
Copy-Item $DotnetUpstreamNotices (Join-Path $LegalDirectory "DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt") -Force

Compress-Archive -Path (Join-Path $PublishDirectory "*") -DestinationPath $Archive -CompressionLevel Optimal -Force

if (-not (Test-Path $Archive -PathType Leaf)) {
    throw "Gelatin package archive was not created."
}

$Hash = (Get-FileHash -Path $Archive -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Published: $PublishDirectory"
Write-Host "Packaged:  $Archive"
Write-Host "SHA256:    $Hash"
Write-Host "SkiaSharp notices: $SkiaUpstreamNotices"
Write-Host ".NET Runtime notices: $DotnetUpstreamNotices"

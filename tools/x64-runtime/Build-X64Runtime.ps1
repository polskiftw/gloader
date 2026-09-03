param(
    [Parameter(Mandatory = $true)]
    [string]$TerrariaDirectory,

    [string]$OutputDirectory,

    [string]$WorkspaceDirectory = (Join-Path $env:LOCALAPPDATA "gloader\x64-runtime-workspace"),

    [switch]$KeepGeneratedSource
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Terraria Unified v0.3.3 is the tagged 1.4.5.8 workspace we audited.
# We use only its Terraria -> TerrariaNetCore patch stages. We deliberately do
# NOT apply its Unified gameplay/QoL patches or its tModLoader patches.
$UpstreamRepository = "https://github.com/gold-meridian/terraria-unified.git"
$UpstreamTag = "v0.3.3"
$UpstreamCommit = "f98c9a42a59c15022cea3f6ad3750d1f85578f61"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Get-DefaultOutputDirectory {
    # Installed layout: walk upward until we find the one public gloader.exe,
    # then put the private game runtime under its gdeps folder.
    $cursor = $PSScriptRoot
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path (Join-Path $cursor "gloader.exe") -PathType Leaf) {
            return (Join-Path $cursor "gdeps\x64-runtime")
        }

        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            break
        }
        $cursor = $parent
    }

    # Source-tree fallback. After build.ps1 has run, prefer the package under
    # dist/gloader so testing the builder does not dirty the repository root.
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $distLoader = Join-Path $repoRoot "dist\gloader"
    if (Test-Path (Join-Path $distLoader "gloader.exe") -PathType Leaf) {
        return (Join-Path $distLoader "gdeps\x64-runtime")
    }

    return (Join-Path $repoRoot "gdeps\x64-runtime")
}

$TerrariaDirectory = [System.IO.Path]::GetFullPath($TerrariaDirectory)
$TerrariaExe = Join-Path $TerrariaDirectory "Terraria.exe"

if (-not (Test-Path $TerrariaExe -PathType Leaf)) {
    throw "Terraria.exe was not found in '$TerrariaDirectory'."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Get-DefaultOutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$WorkspaceDirectory = [System.IO.Path]::GetFullPath($WorkspaceDirectory)

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to build the x64 runtime."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required to build the x64 runtime."
}

$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $dotnetVersion.StartsWith("10.")) {
    throw ".NET 10 SDK is required. Found '$dotnetVersion'."
}

Write-Host ""
Write-Host "gloader x64 Terraria runtime builder"
Write-Host "Terraria source: $TerrariaExe"
Write-Host "Output:          $OutputDirectory"
Write-Host "Workspace:       $WorkspaceDirectory"
Write-Host "Upstream:        terraria-unified $UpstreamTag @ $UpstreamCommit"
Write-Host "Patch ceiling:   TerrariaNetCore only (no Unified gameplay patches, no tML)"
Write-Host ""

if (-not (Test-Path (Join-Path $WorkspaceDirectory ".git"))) {
    if (Test-Path $WorkspaceDirectory) {
        Remove-Item $WorkspaceDirectory -Recurse -Force
    }

    $parent = Split-Path -Parent $WorkspaceDirectory
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Invoke-Checked -FilePath "git" -Arguments @(
        "clone", "--recursive", "--branch", $UpstreamTag, "--depth", "1",
        $UpstreamRepository, $WorkspaceDirectory)
}
else {
    Invoke-Checked -FilePath "git" -Arguments @(
        "-C", $WorkspaceDirectory, "fetch", "origin", "tag", $UpstreamTag, "--depth", "1")
}

Invoke-Checked -FilePath "git" -Arguments @(
    "-C", $WorkspaceDirectory, "checkout", "--detach", $UpstreamCommit)

$actualCommit = (& git -C $WorkspaceDirectory rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $UpstreamCommit) {
    throw "Pinned upstream verification failed. Expected $UpstreamCommit, got '$actualCommit'."
}

Invoke-Checked -FilePath "git" -Arguments @(
    "-C", $WorkspaceDirectory, "submodule", "sync", "--recursive")
Invoke-Checked -FilePath "git" -Arguments @(
    "-C", $WorkspaceDirectory, "submodule", "update", "--init", "--recursive", "--depth", "1")

$SetupCli = Join-Path $WorkspaceDirectory "setup-cli.bat"
if (-not (Test-Path $SetupCli -PathType Leaf)) {
    throw "The pinned upstream workspace does not contain setup-cli.bat."
}

# Generate source from the user's own installed 1.4.5.8 executable. If the
# matching TerrariaServer.exe is absent, the pinned upstream setup retrieves
# that exact server version from Re-Logic's terraria.org dedicated-server API.
Invoke-Checked -FilePath $SetupCli -Arguments @(
    "decompile", "--no-prompts", "--plain-progress",
    "--terraria-steam-dir", $TerrariaDirectory)

# Apply only the vanilla cleanup stage and platform/runtime port.
Invoke-Checked -FilePath $SetupCli -Arguments @(
    "patch", "terraria", "--no-prompts", "--strict", "--plain-progress")
Invoke-Checked -FilePath $SetupCli -Arguments @(
    "patch", "netcore", "--no-prompts", "--strict", "--plain-progress")

$Project = Join-Path $WorkspaceDirectory "src\TerrariaNetCore\Terraria\Terraria.csproj"
if (-not (Test-Path $Project -PathType Leaf)) {
    throw "TerrariaNetCore project was not generated at '$Project'."
}

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# TerrariaNetCore's project has an AfterBuild install target. Override
# TerrariaSteamPath so it installs into gloader's private runtime directory
# rather than touching the real Steam Terraria installation.
Invoke-Checked -FilePath "dotnet" -Arguments @(
    "build", $Project,
    "-c", "Release",
    "-p:TerrariaSteamPath=$OutputDirectory",
    "-p:PlatformTarget=AnyCPU")

$ManagedTarget = Join-Path $OutputDirectory "TerrariaRelease.dll"
if (-not (Test-Path $ManagedTarget -PathType Leaf)) {
    throw "Build completed but TerrariaRelease.dll was not installed into '$OutputDirectory'."
}

$manifest = [ordered]@{
    format = 1
    terraria_sha256 = Get-Sha256 $TerrariaExe
    terraria_file_version = (Get-Item $TerrariaExe).VersionInfo.FileVersion
    upstream_repository = $UpstreamRepository
    upstream_tag = $UpstreamTag
    upstream_commit = $UpstreamCommit
    patch_stage = "TerrariaNetCore"
    unified_gameplay_patches = $false
    tmodloader_patches = $false
    target = "TerrariaRelease.dll"
    architecture = "x64-hosted AnyCPU"
    runtime = ".NET 10 / FNA"
    generated_utc = [DateTime]::UtcNow.ToString("o")
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory "gloader-x64-runtime.json")

if (-not $KeepGeneratedSource) {
    $generated = Join-Path $WorkspaceDirectory "src\decompiled"
    if (Test-Path $generated) {
        Remove-Item $generated -Recurse -Force
    }
}

Write-Host ""
Write-Host "DONE."
Write-Host "Built managed target: $ManagedTarget"
Write-Host "gloader will prefer this runtime automatically when it is under gdeps\x64-runtime."

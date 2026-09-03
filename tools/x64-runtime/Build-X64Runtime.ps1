param(
    [Parameter(Mandatory = $true)]
    [string]$TerrariaDirectory,

    [string]$OutputDirectory,

    [string]$WorkspaceDirectory = (Join-Path $env:LOCALAPPDATA "gloader\x64-runtime-workspace"),

    [switch]$KeepGeneratedSource
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Terraria Unified v0.3.3 is the first tagged release for Terraria 1.4.5.8.
# We use only its Terraria -> TerrariaNetCore patch stages. We deliberately do
# NOT apply its Unified patches or tModLoader patches, so the generated runtime
# is vanilla Terraria code with the modern CoreCLR/FNA platform port only.
$UpstreamRepository = "https://github.com/gold-meridian/terraria-unified.git"
$UpstreamCommit = "f98c9a42a59c15022cea3f6ad3750d1f85578f61"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE: $FilePath $($Arguments -join ' ')"
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

$TerrariaDirectory = [System.IO.Path]::GetFullPath($TerrariaDirectory)
$TerrariaExe = Join-Path $TerrariaDirectory "Terraria.exe"
$TerrariaServerExe = Join-Path $TerrariaDirectory "TerrariaServer.exe"

if (-not (Test-Path $TerrariaExe -PathType Leaf)) {
    throw "Terraria.exe was not found in '$TerrariaDirectory'."
}
if (-not (Test-Path $TerrariaServerExe -PathType Leaf)) {
    throw "TerrariaServer.exe was not found in '$TerrariaDirectory'. The upstream decompiler expects the normal Steam Terraria install."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $candidateRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputDirectory = Join-Path $candidateRoot "gdeps\x64-runtime"
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
Write-Host "Upstream:        terraria-unified $UpstreamCommit (TerrariaNetCore stage only)"
Write-Host ""

if (-not (Test-Path (Join-Path $WorkspaceDirectory ".git"))) {
    if (Test-Path $WorkspaceDirectory) {
        Remove-Item $WorkspaceDirectory -Recurse -Force
    }

    $parent = Split-Path -Parent $WorkspaceDirectory
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Invoke-Checked git clone --recursive $UpstreamRepository $WorkspaceDirectory
}

Invoke-Checked git -C $WorkspaceDirectory fetch origin $UpstreamCommit --depth 1
Invoke-Checked git -C $WorkspaceDirectory checkout --detach $UpstreamCommit
Invoke-Checked git -C $WorkspaceDirectory submodule sync --recursive
Invoke-Checked git -C $WorkspaceDirectory submodule update --init --recursive --depth 1

$SetupCli = Join-Path $WorkspaceDirectory "setup-cli.bat"
if (-not (Test-Path $SetupCli -PathType Leaf)) {
    throw "The pinned upstream workspace does not contain setup-cli.bat."
}

# Generate exact 1.4.5.8 source from the user's own Terraria installation.
Invoke-Checked $SetupCli decompile --no-prompts --plain-progress --terraria-steam-dir $TerrariaDirectory

# Apply only the vanilla cleanup stage and the platform/runtime port. Do not
# apply `patch unified` or `patch tml`.
Invoke-Checked $SetupCli patch terraria --no-prompts --strict --plain-progress
Invoke-Checked $SetupCli patch netcore --no-prompts --strict --plain-progress

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
Invoke-Checked dotnet build $Project -c Release -p:TerrariaSteamPath=$OutputDirectory -p:PlatformTarget=AnyCPU

$ManagedTarget = Join-Path $OutputDirectory "TerrariaRelease.dll"
if (-not (Test-Path $ManagedTarget -PathType Leaf)) {
    throw "Build completed but TerrariaRelease.dll was not installed into '$OutputDirectory'."
}

$manifest = [ordered]@{
    format = 1
    terraria_sha256 = Get-Sha256 $TerrariaExe
    terraria_file_version = (Get-Item $TerrariaExe).VersionInfo.FileVersion
    upstream_repository = $UpstreamRepository
    upstream_commit = $UpstreamCommit
    patch_stage = "TerrariaNetCore"
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

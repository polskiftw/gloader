[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TerrariaInput,

    [string]$OutputDirectory = '',

    [string]$ExpectedVersion = '',

    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$bundleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $bundleRoot 'output'
}

$TerrariaInput = [System.IO.Path]::GetFullPath($TerrariaInput)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$dotnet = Join-Path $bundleRoot 'runtime\dotnet.exe'
$baseRefs = Join-Path $bundleRoot 'refs'
$auditScript = Join-Path $bundleRoot 'Audit-Offline.ps1'
$ilspy = Get-ChildItem -Path (Join-Path $bundleRoot 'ilspy') -Recurse -File -Filter 'ilspycmd.dll' | Select-Object -First 1

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "Bundled .NET runtime is missing: $dotnet"
}
if (-not $ilspy) {
    throw 'Bundled ILSpyCmd is missing.'
}
if (-not (Test-Path -LiteralPath $baseRefs -PathType Container)) {
    throw "Bundled reference pack is missing: $baseRefs"
}
if (-not (Test-Path -LiteralPath $auditScript -PathType Leaf)) {
    throw "Bundled audit script is missing: $auditScript"
}
if (-not (Test-Path -LiteralPath $TerrariaInput)) {
    throw "Terraria input not found: $TerrariaInput"
}

$work = Join-Path $OutputDirectory 'work'
$bootstrap = Join-Path $work 'bootstrap'
$refs = Join-Path $work 'refs'
$inputStage = Join-Path $work 'input'
$source = Join-Path $OutputDirectory 'source'
$audit = Join-Path $OutputDirectory 'audit'

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory, $work, $bootstrap, $refs, $inputStage, $source, $audit | Out-Null
Copy-Item -Path (Join-Path $baseRefs '*') -Destination $refs -Recurse -Force

$inputItem = Get-Item -LiteralPath $TerrariaInput
if ($inputItem.PSIsContainer) {
    $terrariaRoot = $inputItem.FullName
    $terrariaExePath = Join-Path $terrariaRoot 'Terraria.exe'
    if (-not (Test-Path -LiteralPath $terrariaExePath -PathType Leaf)) {
        throw "Terraria.exe was not found directly inside: $terrariaRoot"
    }
    $terrariaExe = Get-Item -LiteralPath $terrariaExePath
}
elseif ([System.IO.Path]::GetExtension($inputItem.FullName).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -Path $inputItem.FullName -DestinationPath $inputStage -Force
    $terrariaExe = Get-ChildItem -Path $inputStage -Recurse -File -Filter 'Terraria.exe' | Select-Object -First 1
    if (-not $terrariaExe) {
        throw 'The input ZIP did not contain Terraria.exe.'
    }
    $terrariaRoot = $terrariaExe.Directory.FullName
}
else {
    if ($inputItem.Name -ne 'Terraria.exe') {
        Write-Warning "Input file is not named Terraria.exe: $($inputItem.FullName)"
    }
    $terrariaExe = $inputItem
    $terrariaRoot = $terrariaExe.Directory.FullName
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($terrariaExe.FullName).FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersion)) {
    $fileVersion = 'unknown'
}
Set-Content -Path (Join-Path $OutputDirectory 'version.txt') -Value $fileVersion -Encoding ASCII
Write-Host "Terraria file version: $fileVersion"
Write-Host "Terraria install/reference root: $terrariaRoot"
Write-Host 'Offline bundle mode: no dependency downloads will be performed.'

if ($ExpectedVersion -and -not $fileVersion.StartsWith($ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Terraria version mismatch. Expected '$ExpectedVersion', got '$fileVersion'."
}

function Invoke-Ilspy {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $dotnet $ilspy.FullName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ILSpyCmd failed with exit code $LASTEXITCODE."
    }
}

# Terraria's install directory is the preferred source for game-specific managed
# references. This picks up the genuine Content Pipeline assembly (and any future
# managed sibling DLLs) without redistributing those files in this bundle.
$managedInstallRefs = New-Object System.Collections.Generic.List[object]
$nativeInstallDlls = New-Object System.Collections.Generic.List[object]
Get-ChildItem -LiteralPath $terrariaRoot -File -Filter '*.dll' | Sort-Object Name | ForEach-Object {
    try {
        $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        $targetName = $assembly.Name + '.dll'
        Copy-Item $_.FullName (Join-Path $refs $targetName) -Force
        $managedInstallRefs.Add([pscustomobject]@{
            file = $_.Name
            assembly = $assembly.Name
            version = $assembly.Version.ToString()
            target = $targetName
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    catch {
        $nativeInstallDlls.Add([pscustomobject]@{
            file = $_.Name
            bytes = $_.Length
        })
    }
}
Write-Host "Harvested $($managedInstallRefs.Count) managed sibling reference assembly/assemblies from the Terraria install."
if ($nativeInstallDlls.Count -gt 0) {
    Write-Host "Ignored $($nativeInstallDlls.Count) native/non-managed sibling DLL(s) for ILSpy reference resolution."
}

$contentPipelinePath = Join-Path $refs 'Microsoft.Xna.Framework.Content.Pipeline.dll'
if (Test-Path -LiteralPath $contentPipelinePath -PathType Leaf) {
    $fromInstall = @($managedInstallRefs | Where-Object { $_.assembly -eq 'Microsoft.Xna.Framework.Content.Pipeline' }).Count -gt 0
    if ($fromInstall) {
        Write-Host 'Using Terraria installation copy of Microsoft.Xna.Framework.Content.Pipeline.dll.'
    }
}
else {
    Write-Warning 'Microsoft.Xna.Framework.Content.Pipeline.dll was not found in the Terraria install or bundled fallback references. If this Terraria version references it, the audit may report unresolved types.'
}

Write-Host 'Pass 1/2: recovering Terraria embedded managed dependencies...'
Invoke-Ilspy -Arguments @(
    '--disable-updatecheck',
    '--ignore-decompilation-errors',
    '-p',
    '-r', $refs,
    '-o', $bootstrap,
    $terrariaExe.FullName
)

$embeddedRefs = New-Object System.Collections.Generic.List[object]
Get-ChildItem -Path $bootstrap -Recurse -File -Filter '*.dll' | ForEach-Object {
    try {
        $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        if ($assembly.Name) {
            $targetName = $assembly.Name + '.dll'
            Copy-Item $_.FullName (Join-Path $refs $targetName) -Force
            $embeddedRefs.Add([pscustomobject]@{
                file = $_.Name
                assembly = $assembly.Name
                version = $assembly.Version.ToString()
                target = $targetName
            })
        }
    }
    catch {
        Write-Verbose "Skipping non-managed DLL resource: $($_.FullName)"
    }
}
Write-Host "Recovered $($embeddedRefs.Count) embedded managed reference assemblies."

Write-Host 'Pass 2/2: clean decompile with install + embedded + bundled fallback references...'
Invoke-Ilspy -Arguments @(
    '--disable-updatecheck',
    '-p',
    '-r', $refs,
    '-o', $source,
    $terrariaExe.FullName
)

$auditResult = & $auditScript -SourceDirectory $source -OutputDirectory $audit -TerrariaVersion $fileVersion

$referenceReport = [pscustomobject]@{
    terraria_version = $fileVersion
    terraria_root = $terrariaRoot
    managed_install_references = @($managedInstallRefs)
    ignored_native_install_dlls = @($nativeInstallDlls)
    embedded_managed_references = @($embeddedRefs)
    content_pipeline_from_install = (@($managedInstallRefs | Where-Object { $_.assembly -eq 'Microsoft.Xna.Framework.Content.Pipeline' }).Count -gt 0)
}
$referenceReport | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $audit 'reference-sources.json') -Encoding UTF8

$safeVersion = ($fileVersion -replace '[^0-9A-Za-z._-]', '_')
$zipPath = Join-Path $OutputDirectory "TerrariaDecomp-$safeVersion-clean.zip"
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

Write-Host ''
Write-Host "Clean source ZIP: $zipPath"
Write-Host "Audit report: $(Join-Path $audit 'audit.md')"
Write-Host "Reference report: $(Join-Path $audit 'reference-sources.json')"

if (-not $KeepWork) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$auditResult

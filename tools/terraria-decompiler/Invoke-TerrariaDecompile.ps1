[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TerrariaInput,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ExpectedVersion = '',

    [string]$ReferencesDirectory = '',

    [string]$XnaInstaller = '',

    [string]$IlspyVersion = '11.0.0.9375',

    [switch]$NoSourceZip,

    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$TerrariaInput = [System.IO.Path]::GetFullPath($TerrariaInput)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $OutputDirectory 'work'
$bootstrap = Join-Path $work 'bootstrap'
$source = Join-Path $OutputDirectory 'source'
$audit = Join-Path $OutputDirectory 'audit'
$tools = Join-Path $work 'tools'
$inputStage = Join-Path $work 'input'

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory, $work, $bootstrap, $source, $audit, $tools, $inputStage | Out-Null

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK/runtime was not found. Install .NET 10 or use the GitHub Actions workflow.'
}

if (-not (Test-Path -LiteralPath $TerrariaInput)) {
    throw "Terraria input not found: $TerrariaInput"
}

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
    if (-not $terrariaExe) { throw 'The input ZIP did not contain Terraria.exe.' }
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
if ([string]::IsNullOrWhiteSpace($fileVersion)) { $fileVersion = 'unknown' }
Set-Content -Path (Join-Path $OutputDirectory 'version.txt') -Value $fileVersion -Encoding ASCII
Write-Host "Terraria file version: $fileVersion"
Write-Host "Terraria install/reference root: $terrariaRoot"

if ($ExpectedVersion -and -not $fileVersion.StartsWith($ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Terraria version mismatch. Expected '$ExpectedVersion', got '$fileVersion'."
}

Write-Host "Installing ilspycmd $IlspyVersion into isolated work directory..."
& dotnet tool install ilspycmd --tool-path $tools --version $IlspyVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install ilspycmd failed with exit code $LASTEXITCODE." }
$ilspy = Join-Path $tools 'ilspycmd.exe'
if (-not (Test-Path -LiteralPath $ilspy)) { throw "ilspycmd executable not found at $ilspy" }

if ([string]::IsNullOrWhiteSpace($ReferencesDirectory)) {
    $ReferencesDirectory = Join-Path $work 'refs'
    $prepareArgs = @{
        OutputDirectory = $ReferencesDirectory
        WorkDirectory = (Join-Path $work 'ref-work')
    }
    if ($XnaInstaller) { $prepareArgs.XnaInstaller = $XnaInstaller }
    if ($KeepWork) { $prepareArgs.KeepWork = $true }
    & (Join-Path $scriptRoot 'Prepare-References.ps1') @prepareArgs
}
$ReferencesDirectory = [System.IO.Path]::GetFullPath($ReferencesDirectory)
if (-not (Test-Path -LiteralPath $ReferencesDirectory -PathType Container)) {
    throw "References directory not found: $ReferencesDirectory"
}

# Prefer the exact managed assemblies shipped beside Terraria.exe. Native DLLs are
# useful to Terraria at runtime but do not provide CLR metadata to ILSpy.
$managedInstallRefs = New-Object System.Collections.Generic.List[object]
$nativeInstallDlls = New-Object System.Collections.Generic.List[object]
Get-ChildItem -LiteralPath $terrariaRoot -File -Filter '*.dll' | Sort-Object Name | ForEach-Object {
    try {
        $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        $targetName = $assembly.Name + '.dll'
        Copy-Item $_.FullName (Join-Path $ReferencesDirectory $targetName) -Force
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

Write-Host 'Running bootstrap ILSpy pass to recover embedded managed dependencies...'
& $ilspy --disable-updatecheck --ignore-decompilation-errors -p -r $ReferencesDirectory -o $bootstrap $terrariaExe.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap ILSpy pass failed with exit code $LASTEXITCODE."
}

$embeddedRefs = New-Object System.Collections.Generic.List[object]
Get-ChildItem -Path $bootstrap -Recurse -File -Filter '*.dll' | ForEach-Object {
    try {
        $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        if ($assembly.Name) {
            $targetName = $assembly.Name + '.dll'
            Copy-Item $_.FullName (Join-Path $ReferencesDirectory $targetName) -Force
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

Write-Host 'Running clean ILSpy pass with install + embedded + prepared references...'
& $ilspy --disable-updatecheck -p -r $ReferencesDirectory -o $source $terrariaExe.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Clean ILSpy pass failed with exit code $LASTEXITCODE."
}

$auditResult = & (Join-Path $scriptRoot 'Audit-Decompile.ps1') `
    -SourceDirectory $source `
    -OutputDirectory $audit `
    -TerrariaVersion $fileVersion

$referenceReport = [pscustomobject]@{
    terraria_version = $fileVersion
    terraria_root = $terrariaRoot
    managed_install_references = @($managedInstallRefs)
    ignored_native_install_dlls = @($nativeInstallDlls)
    embedded_managed_references = @($embeddedRefs)
    content_pipeline_from_install = (@($managedInstallRefs | Where-Object { $_.assembly -eq 'Microsoft.Xna.Framework.Content.Pipeline' }).Count -gt 0)
}
$referenceReport | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $audit 'reference-sources.json') -Encoding UTF8

if (-not $NoSourceZip) {
    $safeVersion = ($fileVersion -replace '[^0-9A-Za-z._-]', '_')
    $zipPath = Join-Path $OutputDirectory "TerrariaDecomp-$safeVersion-clean.zip"
    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
    Write-Host "Clean source ZIP: $zipPath"
}

Write-Host "Audit report: $(Join-Path $audit 'audit.md')"
Write-Host "Reference report: $(Join-Path $audit 'reference-sources.json')"
$auditResult

if (-not $KeepWork) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

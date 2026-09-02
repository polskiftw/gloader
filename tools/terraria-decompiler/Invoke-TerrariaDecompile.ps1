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

if (-not (Test-Path -LiteralPath $TerrariaInput -PathType Leaf)) {
    throw "Terraria input not found: $TerrariaInput"
}

if ([System.IO.Path]::GetExtension($TerrariaInput).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -Path $TerrariaInput -DestinationPath $inputStage -Force
    $terrariaExe = Get-ChildItem -Path $inputStage -Recurse -File -Filter 'Terraria.exe' | Select-Object -First 1
    if (-not $terrariaExe) { throw 'The input ZIP did not contain Terraria.exe.' }
}
else {
    if ((Split-Path $TerrariaInput -Leaf) -ne 'Terraria.exe') {
        Write-Warning "Input file is not named Terraria.exe: $TerrariaInput"
    }
    $terrariaExe = Get-Item -LiteralPath $TerrariaInput
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($terrariaExe.FullName).FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersion)) { $fileVersion = 'unknown' }
Set-Content -Path (Join-Path $OutputDirectory 'version.txt') -Value $fileVersion -Encoding ASCII
Write-Host "Terraria file version: $fileVersion"

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

# Pass 1 intentionally runs without Terraria's embedded managed libraries. ILSpy still
# exports those raw DLL resources, which we then canonicalize by real assembly name.
Write-Host 'Running bootstrap ILSpy pass to recover embedded managed dependencies...'
& $ilspy --disable-updatecheck --ignore-decompilation-errors -p -o $bootstrap $terrariaExe.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap ILSpy pass failed with exit code $LASTEXITCODE."
}

$embeddedCount = 0
Get-ChildItem -Path $bootstrap -Recurse -File -Filter '*.dll' | ForEach-Object {
    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName).Name
        if ($assemblyName) {
            $target = Join-Path $ReferencesDirectory ($assemblyName + '.dll')
            Copy-Item $_.FullName $target -Force
            $embeddedCount++
        }
    }
    catch {
        Write-Verbose "Skipping non-managed DLL resource: $($_.FullName)"
    }
}
Write-Host "Recovered $embeddedCount embedded managed reference assemblies."

Write-Host 'Running clean ILSpy pass with complete reference directory...'
& $ilspy --disable-updatecheck -p -r $ReferencesDirectory -o $source $terrariaExe.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Clean ILSpy pass failed with exit code $LASTEXITCODE."
}

$auditResult = & (Join-Path $scriptRoot 'Audit-Decompile.ps1') `
    -SourceDirectory $source `
    -OutputDirectory $audit `
    -TerrariaVersion $fileVersion

if (-not $NoSourceZip) {
    $safeVersion = ($fileVersion -replace '[^0-9A-Za-z._-]', '_')
    $zipPath = Join-Path $OutputDirectory "TerrariaDecomp-$safeVersion-clean.zip"
    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
    Write-Host "Clean source ZIP: $zipPath"
}

Write-Host "Audit report: $(Join-Path $audit 'audit.md')"
$auditResult

if (-not $KeepWork) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

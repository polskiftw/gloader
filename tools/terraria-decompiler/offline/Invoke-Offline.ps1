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
if (-not (Test-Path -LiteralPath $TerrariaInput -PathType Leaf)) {
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

if ([System.IO.Path]::GetExtension($TerrariaInput).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -Path $TerrariaInput -DestinationPath $inputStage -Force
    $terrariaExe = Get-ChildItem -Path $inputStage -Recurse -File -Filter 'Terraria.exe' | Select-Object -First 1
    if (-not $terrariaExe) {
        throw 'The input ZIP did not contain Terraria.exe.'
    }
}
else {
    $terrariaExe = Get-Item -LiteralPath $TerrariaInput
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($terrariaExe.FullName).FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersion)) {
    $fileVersion = 'unknown'
}
Set-Content -Path (Join-Path $OutputDirectory 'version.txt') -Value $fileVersion -Encoding ASCII
Write-Host "Terraria file version: $fileVersion"
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

Write-Host 'Pass 1/2: recovering Terraria embedded managed dependencies...'
Invoke-Ilspy -Arguments @(
    '--disable-updatecheck',
    '--ignore-decompilation-errors',
    '-p',
    '-o', $bootstrap,
    $terrariaExe.FullName
)

$embeddedCount = 0
Get-ChildItem -Path $bootstrap -Recurse -File -Filter '*.dll' | ForEach-Object {
    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName).Name
        if ($assemblyName) {
            Copy-Item $_.FullName (Join-Path $refs ($assemblyName + '.dll')) -Force
            $embeddedCount++
        }
    }
    catch {
        Write-Verbose "Skipping non-managed DLL resource: $($_.FullName)"
    }
}
Write-Host "Recovered $embeddedCount embedded managed reference assemblies."

Write-Host 'Pass 2/2: clean decompile with bundled references...'
Invoke-Ilspy -Arguments @(
    '--disable-updatecheck',
    '-p',
    '-r', $refs,
    '-o', $source,
    $terrariaExe.FullName
)

$auditResult = & $auditScript -SourceDirectory $source -OutputDirectory $audit -TerrariaVersion $fileVersion

$safeVersion = ($fileVersion -replace '[^0-9A-Za-z._-]', '_')
$zipPath = Join-Path $OutputDirectory "TerrariaDecomp-$safeVersion-clean.zip"
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

Write-Host ''
Write-Host "Clean source ZIP: $zipPath"
Write-Host "Audit report: $(Join-Path $audit 'audit.md')"

if (-not $KeepWork) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$auditResult

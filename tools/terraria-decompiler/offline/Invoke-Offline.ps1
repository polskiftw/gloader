[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TerrariaInput,

    [string]$OutputDirectory = '',

    [string]$ExpectedVersion = '',

    [ValidateSet('Pair', 'Client', 'Server')]
    [string]$TargetMode = 'Pair',

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
$refsBase = Join-Path $work 'refs-base'
$inputStage = Join-Path $work 'input'
$clientRoot = Join-Path $OutputDirectory 'client'
$serverRoot = Join-Path $OutputDirectory 'server'
$auditRoot = Join-Path $OutputDirectory 'audit'
$legacySource = Join-Path $OutputDirectory 'source'
$legacyVersionFile = Join-Path $OutputDirectory 'version.txt'
$ownershipMarker = Join-Path $OutputDirectory '.terraria-decompiler-output'

# The GUI lets the user select any directory. Never delete that directory itself,
# and only clean known output names when this tool previously marked the directory.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (-not (Test-Path -LiteralPath $ownershipMarker -PathType Leaf)) {
    $collisions = New-Object System.Collections.Generic.List[string]
    foreach ($ownedDirectory in @($work, $clientRoot, $serverRoot, $auditRoot, $legacySource)) {
        if (Test-Path -LiteralPath $ownedDirectory) {
            $collisions.Add((Split-Path $ownedDirectory -Leaf))
        }
    }
    if (Test-Path -LiteralPath $legacyVersionFile) {
        $collisions.Add('version.txt')
    }
    Get-ChildItem -LiteralPath $OutputDirectory -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like 'TerrariaDecomp-*-clean.zip' -or
            $_.Name -like 'TerrariaClientDecomp-*-clean.zip' -or
            $_.Name -like 'TerrariaServerDecomp-*-clean.zip'
        } |
        ForEach-Object { $collisions.Add($_.Name) }

    if ($collisions.Count -gt 0) {
        throw "The selected output folder contains files/folders that use Terraria Decompiler output names, but the folder was not created by this tool: $($collisions -join ', '). Choose a clean/dedicated output folder; nothing was deleted."
    }

    Set-Content -Path $ownershipMarker -Value 'gloader Terraria Decompiler output directory' -Encoding ASCII
}
else {
    foreach ($ownedDirectory in @($work, $clientRoot, $serverRoot, $auditRoot, $legacySource)) {
        if (Test-Path -LiteralPath $ownedDirectory) {
            Remove-Item -LiteralPath $ownedDirectory -Recurse -Force
        }
    }
    if (Test-Path -LiteralPath $legacyVersionFile -PathType Leaf) {
        Remove-Item -LiteralPath $legacyVersionFile -Force
    }
    Get-ChildItem -LiteralPath $OutputDirectory -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like 'TerrariaDecomp-*-clean.zip' -or
            $_.Name -like 'TerrariaClientDecomp-*-clean.zip' -or
            $_.Name -like 'TerrariaServerDecomp-*-clean.zip'
        } |
        Remove-Item -Force
}

New-Item -ItemType Directory -Force -Path $work, $refsBase, $inputStage, $auditRoot | Out-Null
Copy-Item -Path (Join-Path $baseRefs '*') -Destination $refsBase -Recurse -Force

$inputItem = Get-Item -LiteralPath $TerrariaInput
if ($inputItem.PSIsContainer) {
    $terrariaRoot = $inputItem.FullName
}
elseif ([System.IO.Path]::GetExtension($inputItem.FullName).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -Path $inputItem.FullName -DestinationPath $inputStage -Force
    $anchor = $null
    if ($TargetMode -ne 'Server') {
        $anchor = Get-ChildItem -Path $inputStage -Recurse -File -Filter 'Terraria.exe' | Select-Object -First 1
    }
    if (-not $anchor) {
        $anchor = Get-ChildItem -Path $inputStage -Recurse -File -Filter 'TerrariaServer.exe' | Select-Object -First 1
    }
    if (-not $anchor) {
        throw 'The input ZIP did not contain Terraria.exe or TerrariaServer.exe.'
    }
    $terrariaRoot = $anchor.Directory.FullName
}
else {
    $terrariaRoot = $inputItem.Directory.FullName
}

$clientExePath = Join-Path $terrariaRoot 'Terraria.exe'
$serverExePath = Join-Path $terrariaRoot 'TerrariaServer.exe'

if (-not $inputItem.PSIsContainer -and -not [System.IO.Path]::GetExtension($inputItem.FullName).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    if ($inputItem.Name.Equals('Terraria.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $clientExePath = $inputItem.FullName
    }
    elseif ($inputItem.Name.Equals('TerrariaServer.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $serverExePath = $inputItem.FullName
    }
}

$clientRequired = $TargetMode -eq 'Pair' -or $TargetMode -eq 'Client'
$serverRequired = $TargetMode -eq 'Pair' -or $TargetMode -eq 'Server'

if ($clientRequired -and -not (Test-Path -LiteralPath $clientExePath -PathType Leaf)) {
    throw "Terraria.exe was not found beside the selected input: $terrariaRoot"
}
if ($serverRequired -and -not (Test-Path -LiteralPath $serverExePath -PathType Leaf)) {
    throw "TerrariaServer.exe was not found beside Terraria.exe: $terrariaRoot"
}

function Get-FileVersionSafe {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $value = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path).FileVersion
    if ([string]::IsNullOrWhiteSpace($value)) { return 'unknown' }
    return $value
}

$clientVersion = if (Test-Path -LiteralPath $clientExePath -PathType Leaf) { Get-FileVersionSafe $clientExePath } else { '' }
$serverVersion = if (Test-Path -LiteralPath $serverExePath -PathType Leaf) { Get-FileVersionSafe $serverExePath } else { '' }

if ($TargetMode -eq 'Pair' -and -not $clientVersion.Equals($serverVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Terraria client/server version mismatch. Terraria.exe is '$clientVersion' but TerrariaServer.exe is '$serverVersion'. Update the install so they match before decompiling."
}

if ($ExpectedVersion) {
    foreach ($versionEntry in @(
        [pscustomobject]@{ Name = 'client'; Required = $clientRequired; Version = $clientVersion },
        [pscustomobject]@{ Name = 'server'; Required = $serverRequired; Version = $serverVersion }
    )) {
        if ($versionEntry.Required -and -not $versionEntry.Version.StartsWith($ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Terraria $($versionEntry.Name) version mismatch. Expected '$ExpectedVersion', got '$($versionEntry.Version)'."
        }
    }
}

Write-Host "Terraria install/reference root: $terrariaRoot"
if ($clientRequired) { Write-Host "Client file version: $clientVersion" }
if ($serverRequired) { Write-Host "Server file version: $serverVersion" }
Write-Host 'Offline bundle mode: no dependency downloads will be performed.'

function Invoke-Ilspy {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $dotnet $ilspy.FullName @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "ILSpyCmd failed with exit code $LASTEXITCODE."
    }
}

# These collections stay as ordinary PowerShell arrays because the packaged engine
# runs under Windows PowerShell 5.1. That avoids its generic-List binder edge cases
# when arrays are later embedded into PSCustomObjects / JSON.
$managedInstallRefs = @()
$nativeInstallDlls = @()
$installDllFiles = @(Get-ChildItem -LiteralPath $terrariaRoot -File -Filter '*.dll' | Sort-Object Name)
foreach ($dllFile in $installDllFiles) {
    try {
        $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($dllFile.FullName)
        $referenceFileName = $assembly.Name + '.dll'
        Copy-Item $dllFile.FullName (Join-Path $refsBase $referenceFileName) -Force
        $managedInstallRefs += [pscustomobject]@{
            file = $dllFile.Name
            assembly = $assembly.Name
            version = $assembly.Version.ToString()
            target = $referenceFileName
            sha256 = (Get-FileHash -LiteralPath $dllFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    catch {
        $nativeInstallDlls += [pscustomobject]@{
            file = $dllFile.Name
            bytes = $dllFile.Length
        }
    }
}
Write-Host "Harvested $($managedInstallRefs.Count) managed sibling reference assembly/assemblies from the Terraria install."
if ($nativeInstallDlls.Count -gt 0) {
    Write-Host "Ignored $($nativeInstallDlls.Count) native/non-managed sibling DLL(s) for ILSpy reference resolution."
}

$contentPipelinePath = Join-Path $refsBase 'Microsoft.Xna.Framework.Content.Pipeline.dll'
if (Test-Path -LiteralPath $contentPipelinePath -PathType Leaf) {
    $fromInstall = @($managedInstallRefs | Where-Object { $_.assembly -eq 'Microsoft.Xna.Framework.Content.Pipeline' }).Count -gt 0
    if ($fromInstall) {
        Write-Host 'Using Terraria installation copy of Microsoft.Xna.Framework.Content.Pipeline.dll.'
    }
}
else {
    Write-Warning 'Microsoft.Xna.Framework.Content.Pipeline.dll was not found in the Terraria install or bundled fallback references. If a selected executable references it, that target audit may report unresolved types.'
}

function Get-AuditIssueTotal {
    param([object]$AuditResult)

    $total = 0
    foreach ($property in $AuditResult.counts.PSObject.Properties) {
        $total += [int]$property.Value
    }
    foreach ($property in $AuditResult.legacy_signatures.PSObject.Properties) {
        $total += [int]$property.Value
    }
    return $total
}

function Invoke-TargetDecompile {
    param(
        [Parameter(Mandatory = $true)][string]$TargetName,
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$TargetOutputRoot
    )

    $label = $TargetName.ToUpperInvariant()
    $targetWork = Join-Path $work $TargetName
    $bootstrap = Join-Path $targetWork 'bootstrap'
    $refs = Join-Path $targetWork 'refs'
    $source = Join-Path $TargetOutputRoot 'source'
    $targetAudit = Join-Path $auditRoot $TargetName

    New-Item -ItemType Directory -Force -Path $targetWork, $bootstrap, $refs, $source, $targetAudit, $TargetOutputRoot | Out-Null
    Copy-Item -Path (Join-Path $refsBase '*') -Destination $refs -Recurse -Force

    Write-Host "$label pass 1/2: recovering embedded managed dependencies..."
    Invoke-Ilspy -Arguments @(
        '--disable-updatecheck',
        '--ignore-decompilation-errors',
        '-p',
        '-r', $refs,
        '-o', $bootstrap,
        $ExecutablePath
    )

    $embeddedRefs = @()
    $embeddedDllFiles = @(Get-ChildItem -Path $bootstrap -Recurse -File -Filter '*.dll')
    foreach ($embeddedDll in $embeddedDllFiles) {
        try {
            $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($embeddedDll.FullName)
            if ($assembly.Name) {
                # Do not call this $targetName: PowerShell variable names are case-insensitive,
                # so that would overwrite the $TargetName function parameter.
                $referenceFileName = $assembly.Name + '.dll'
                Copy-Item $embeddedDll.FullName (Join-Path $refs $referenceFileName) -Force
                $embeddedRefs += [pscustomobject]@{
                    file = $embeddedDll.Name
                    assembly = $assembly.Name
                    version = $assembly.Version.ToString()
                    target = $referenceFileName
                }
            }
        }
        catch {
            Write-Verbose "Skipping non-managed DLL resource: $($embeddedDll.FullName)"
        }
    }
    Write-Host "$label recovered $($embeddedRefs.Count) embedded managed reference assemblies."

    Write-Host "$label pass 2/2: clean decompile with install + target embedded + bundled fallback references..."
    Invoke-Ilspy -Arguments @(
        '--disable-updatecheck',
        '-p',
        '-r', $refs,
        '-o', $source,
        $ExecutablePath
    )

    $auditResult = & $auditScript -SourceDirectory $source -OutputDirectory $targetAudit -TerrariaVersion $Version
    $safeVersion = ($Version -replace '[^0-9A-Za-z._-]', '_')
    $displayName = if ($TargetName -eq 'client') { 'TerrariaClientDecomp' } else { 'TerrariaServerDecomp' }
    $zipPath = Join-Path $TargetOutputRoot "$displayName-$safeVersion-clean.zip"
    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
    Write-Host "$label clean source ZIP: $zipPath"

    return [pscustomobject]@{
        name = $TargetName
        executable = $ExecutablePath
        version = $Version
        source = $source
        zip = $zipPath
        audit_directory = $targetAudit
        audit = $auditResult
        issue_count = Get-AuditIssueTotal $auditResult
        embedded_managed_references = $embeddedRefs
    }
}

$results = @()
if ($clientRequired) {
    $results += Invoke-TargetDecompile -TargetName 'client' -ExecutablePath $clientExePath -Version $clientVersion -TargetOutputRoot $clientRoot
}
if ($serverRequired) {
    $results += Invoke-TargetDecompile -TargetName 'server' -ExecutablePath $serverExePath -Version $serverVersion -TargetOutputRoot $serverRoot
}

$countKeys = @(
    'unknown_result_type',
    'encoded_constructor',
    'ref_cast_artifact',
    'failed_decompile',
    'expected_unknown',
    'invalid_unknown_comparison'
)
$legacyKeys = @(
    'old_velocity_statement',
    'old_nullable_num52',
    'old_mouse_text_color_assignment'
)

$combinedCounts = [ordered]@{}
foreach ($key in $countKeys) {
    $sum = 0
    foreach ($entry in $results) {
        $sum += [int]$entry.audit.counts.$key
    }
    $combinedCounts[$key] = $sum
}

$combinedLegacy = [ordered]@{}
foreach ($key in $legacyKeys) {
    $sum = 0
    foreach ($entry in $results) {
        $sum += [int]$entry.audit.legacy_signatures.$key
    }
    $combinedLegacy[$key] = $sum
}

$totalIssues = 0
$totalSourceFiles = 0
$targetSummaries = [ordered]@{}
foreach ($entry in $results) {
    $totalIssues += [int]$entry.issue_count
    $totalSourceFiles += [int]$entry.audit.source_files
    $targetSummaries[$entry.name] = [pscustomobject]@{
        version = $entry.version
        source_files = [int]$entry.audit.source_files
        tracked_issues = [int]$entry.issue_count
        source_directory = "$($entry.name)\source"
        source_zip = "$($entry.name)\$([System.IO.Path]::GetFileName($entry.zip))"
        detailed_audit = "audit\$($entry.name)\audit.md"
    }
}

$combinedAudit = [pscustomobject]@{
    generated_at_utc = [DateTime]::UtcNow.ToString('o')
    target_mode = $TargetMode
    client_version = $clientVersion
    server_version = $serverVersion
    total_source_files = $totalSourceFiles
    total_tracked_issues = $totalIssues
    counts = [pscustomobject]$combinedCounts
    legacy_signatures = [pscustomobject]$combinedLegacy
    targets = [pscustomobject]$targetSummaries
}
$combinedAudit | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $auditRoot 'audit.json') -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Terraria client + server decompile audit')
$md.Add('')
$md.Add("Overall tracked issues: **$totalIssues**")
$md.Add("Total C# files: **$totalSourceFiles**")
$md.Add('')
$md.Add('## Targets')
$md.Add('')
$md.Add('| Target | Version | C# files | Tracked issues |')
$md.Add('|---|---|---:|---:|')
foreach ($entry in $results) {
    $md.Add("| $($entry.name) | $($entry.version) | $($entry.audit.source_files) | $($entry.issue_count) |")
}
$md.Add('')
$md.Add('## Combined decompiler artifact counts')
$md.Add('')
$md.Add('| Artifact | Count |')
$md.Add('|---|---:|')
foreach ($key in $combinedCounts.Keys) {
    $md.Add('| `' + $key + '` | ' + $combinedCounts[$key] + ' |')
}
$md.Add('')
$md.Add('## Combined older-guide signatures')
$md.Add('')
$md.Add('| Signature | Count |')
$md.Add('|---|---:|')
foreach ($key in $combinedLegacy.Keys) {
    $md.Add('| `' + $key + '` | ' + $combinedLegacy[$key] + ' |')
}
$md.Add('')
$md.Add('## Detailed target audits')
$md.Add('')
foreach ($entry in $results) {
    $md.Add('- `' + $entry.name + '\audit.md` - full per-target hits and file details')
}
$md | Set-Content -Path (Join-Path $auditRoot 'audit.md') -Encoding UTF8

$targetReferenceReports = [ordered]@{}
foreach ($entry in $results) {
    $targetReferenceReports[$entry.name] = [pscustomobject]@{
        executable = $entry.executable
        version = $entry.version
        embedded_managed_references = $entry.embedded_managed_references
    }
}

$referenceReport = [pscustomobject]@{
    terraria_root = $terrariaRoot
    client_version = $clientVersion
    server_version = $serverVersion
    managed_install_references = $managedInstallRefs
    ignored_native_install_dlls = $nativeInstallDlls
    content_pipeline_from_install = (@($managedInstallRefs | Where-Object { $_.assembly -eq 'Microsoft.Xna.Framework.Content.Pipeline' }).Count -gt 0)
    targets = [pscustomobject]$targetReferenceReports
}
$referenceReport | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $auditRoot 'reference-sources.json') -Encoding UTF8

Write-Host ''
foreach ($entry in $results) {
    Write-Host "$($entry.name.ToUpperInvariant()) output: $([System.IO.Path]::GetDirectoryName($entry.zip))"
}
Write-Host "Combined audit report: $(Join-Path $auditRoot 'audit.md')"
Write-Host "Combined tracked issues: $totalIssues"
Write-Host "Reference report: $(Join-Path $auditRoot 'reference-sources.json')"

if (-not $KeepWork) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$combinedAudit

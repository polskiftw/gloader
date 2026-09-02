[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$TerrariaVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$files = @(Get-ChildItem -Path $SourceDirectory -Recurse -File -Filter '*.cs')

$patterns = [ordered]@{
    unknown_result_type = 'Unknown result type \(might be due to invalid IL or missing references\)'
    encoded_constructor = '_002Ector'
    ref_cast_artifact = '\(\([A-Za-z0-9_\.<>]+\)\(ref '
    failed_decompile = '(?i)failed to decompile|could not decompile|decompilation failed'
    expected_unknown = 'Expected .+, but got Unknown'
    invalid_unknown_comparison = 'Invalid comparison between Unknown'
}

$counts = [ordered]@{}
$hits = New-Object System.Collections.Generic.List[object]

foreach ($key in $patterns.Keys) {
    $count = 0
    foreach ($file in $files) {
        $matches = Select-String -LiteralPath $file.FullName -Pattern $patterns[$key] -AllMatches
        foreach ($match in $matches) {
            $matchCount = [Math]::Max(1, $match.Matches.Count)
            $count += $matchCount
            $relative = [System.IO.Path]::GetRelativePath($SourceDirectory, $file.FullName)
            $hits.Add([pscustomobject]@{
                kind = $key
                file = $relative
                line = $match.LineNumber
                text = $match.Line.Trim()
                occurrences = $matchCount
            })
        }
    }
    $counts[$key] = $count
}

$legacyPatterns = [ordered]@{
    old_velocity_statement = 'this\.velocity\s*!=\s*value2\s*;'
    old_nullable_num52 = 'num52\s*\?\?\s*-1'
    old_mouse_text_color_assignment = 'color\.R\s*=\s*Main\.mouseTextColor\s*/\s*2'
}
$legacyCounts = [ordered]@{}
foreach ($key in $legacyPatterns.Keys) {
    $legacyCount = 0
    foreach ($file in $files) {
        $legacyMatches = Select-String -LiteralPath $file.FullName -Pattern $legacyPatterns[$key] -AllMatches
        foreach ($match in $legacyMatches) {
            $legacyCount += [Math]::Max(1, $match.Matches.Count)
        }
    }
    $legacyCounts[$key] = $legacyCount
}

$byFile = $hits |
    Group-Object file |
    ForEach-Object {
        [pscustomobject]@{
            file = $_.Name
            hits = ($_.Group | Measure-Object occurrences -Sum).Sum
            kinds = @($_.Group.kind | Sort-Object -Unique)
        }
    } |
    Sort-Object hits -Descending

$result = [pscustomobject]@{
    terraria_version = $TerrariaVersion
    generated_at_utc = [DateTime]::UtcNow.ToString('o')
    source_files = $files.Count
    counts = [pscustomobject]$counts
    legacy_signatures = [pscustomobject]$legacyCounts
    files_with_hits = @($byFile).Count
    by_file = @($byFile)
    hits = @($hits)
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $OutputDirectory 'audit.json') -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Terraria decompile audit')
$md.Add('')
if ($TerrariaVersion) { $md.Add("Terraria version: **$TerrariaVersion**") }
$md.Add("C# files: **$($files.Count)**")
$md.Add('')
$md.Add('## Decompiler artifact counts')
$md.Add('')
$md.Add('| Artifact | Count |')
$md.Add('|---|---:|')
foreach ($key in $counts.Keys) {
    $md.Add('| `' + $key + '` | ' + $counts[$key] + ' |')
}
$md.Add('')
$md.Add('## Older-guide signatures')
$md.Add('')
$md.Add('| Signature | Count |')
$md.Add('|---|---:|')
foreach ($key in $legacyCounts.Keys) {
    $md.Add('| `' + $key + '` | ' + $legacyCounts[$key] + ' |')
}
$md.Add('')
$md.Add('## Files with remaining diagnostics')
$md.Add('')
if (@($byFile).Count -eq 0) {
    $md.Add('None.')
}
else {
    foreach ($entry in ($byFile | Select-Object -First 50)) {
        $md.Add('- `' + $entry.file + '` — ' + $entry.hits + ' hit(s): ' + ($entry.kinds -join ', '))
    }
}
$md | Set-Content -Path (Join-Path $OutputDirectory 'audit.md') -Encoding UTF8

$result

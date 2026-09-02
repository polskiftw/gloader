[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$WorkDirectory = '',

    [string]$IlspyVersion = '11.0.0.9375',

    [string]$DotnetRuntimeVersion = '10.0.11'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('gloader-terraria-offline-' + [guid]::NewGuid().ToString('N'))
}
$WorkDirectory = [System.IO.Path]::GetFullPath($WorkDirectory)

$bundleName = 'TerrariaDecompilerOffline-win-x64'
$stage = Join-Path $WorkDirectory $bundleName
$refs = Join-Path $stage 'refs'
$runtime = Join-Path $stage 'runtime'
$ilspyOut = Join-Path $stage 'ilspy'
$licenses = Join-Path $stage 'licenses'

$Net40PackageUrl = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net40/1.0.3/microsoft.netframework.referenceassemblies.net40.1.0.3.nupkg'
$XnaRedistUrl = 'https://download.microsoft.com/download/5/3/A/53A804C8-EC78-43CD-A0F0-2FB4D45603D3/xnafx40_redist.msi'
$DotnetInstallUrl = 'https://dot.net/v1/dotnet-install.ps1'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is required to BUILD the offline bundle.'
}
$sevenZip = Get-Command '7z.exe' -ErrorAction SilentlyContinue
if (-not $sevenZip) {
    $sevenZipPath = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
    if (Test-Path -LiteralPath $sevenZipPath) { $sevenZip = Get-Item -LiteralPath $sevenZipPath }
}
if (-not $sevenZip) {
    throw '7-Zip is required to BUILD the offline bundle.'
}
$sevenZipExe = if ($sevenZip.PSObject.Properties.Name -contains 'Source') { $sevenZip.Source } else { $sevenZip.FullName }

if (Test-Path -LiteralPath $WorkDirectory) {
    Remove-Item -LiteralPath $WorkDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory, $WorkDirectory, $stage, $refs, $runtime, $ilspyOut, $licenses | Out-Null

try {
    Write-Host 'Bundling .NET Framework 4.0 reference assemblies...'
    $netPkg = Join-Path $WorkDirectory 'net40.nupkg'
    $netZip = Join-Path $WorkDirectory 'net40.zip'
    $netExtract = Join-Path $WorkDirectory 'net40'
    Invoke-WebRequest -Uri $Net40PackageUrl -OutFile $netPkg
    Copy-Item $netPkg $netZip -Force
    Expand-Archive -Path $netZip -DestinationPath $netExtract -Force

    $mscorlib = Get-ChildItem -Path $netExtract -Recurse -File -Filter 'mscorlib.dll' |
        Where-Object { $_.FullName -match '[\\/]v4\.0[\\/]' } |
        Select-Object -First 1
    if (-not $mscorlib) {
        throw 'Could not locate .NET Framework 4.0 references in the NuGet package.'
    }
    Get-ChildItem -Path $mscorlib.Directory.FullName -File -Filter '*.dll' |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $refs $_.Name) -Force }
    Get-ChildItem -Path $netExtract -Recurse -File |
        Where-Object { $_.Name -match '^(LICENSE|LICENSE\.txt|ThirdPartyNotices)' } |
        Select-Object -First 3 |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses ('net40-' + $_.Name)) -Force }

    Write-Host 'Bundling redistributable XNA Framework 4.0 Refresh runtime assemblies...'
    $xnaMsi = Join-Path $WorkDirectory 'xnafx40_redist.msi'
    $xnaExtract = Join-Path $WorkDirectory 'xna-redist'
    Invoke-WebRequest -Uri $XnaRedistUrl -OutFile $xnaMsi
    $signature = Get-AuthenticodeSignature -FilePath $xnaMsi
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw "XNA redistributable signature verification failed: $($signature.Status)"
    }
    New-Item -ItemType Directory -Force -Path $xnaExtract | Out-Null
    & $sevenZipExe x $xnaMsi "-o$xnaExtract" -y | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to unpack XNA redistributable with exit code $LASTEXITCODE."
    }

    Get-ChildItem -Path $xnaExtract -Recurse -File | ForEach-Object {
        try {
            $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
            if ($assembly.Name -like 'Microsoft.Xna.Framework*') {
                $targetName = $assembly.Name + '.dll'
                Copy-Item $_.FullName (Join-Path $refs $targetName) -Force
            }
        }
        catch {
        }
    }

    $requiredRuntimeXna = @(
        'Microsoft.Xna.Framework.dll',
        'Microsoft.Xna.Framework.Game.dll',
        'Microsoft.Xna.Framework.Graphics.dll',
        'Microsoft.Xna.Framework.Xact.dll'
    )
    foreach ($name in $requiredRuntimeXna) {
        if (-not (Test-Path -LiteralPath (Join-Path $refs $name))) {
            throw "XNA runtime assembly was not recovered from the redistributable: $name"
        }
    }

    Get-ChildItem -Path $xnaExtract -Recurse -File |
        Where-Object { $_.Extension -in @('.rtf', '.txt') -and $_.Name -match '(?i)license|eula|terms' } |
        Select-Object -First 5 |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses ('xna-' + $_.Name)) -Force }

    # Deliberately do not bundle Microsoft.Xna.Framework.Content.Pipeline.dll or a shim.
    # Current Terraria ships the genuine assembly next to Terraria.exe. The runtime
    # decompiler harvests every managed sibling DLL from the user's own install first.
    $pipelineFallback = Join-Path $refs 'Microsoft.Xna.Framework.Content.Pipeline.dll'
    if (Test-Path -LiteralPath $pipelineFallback) {
        Remove-Item -LiteralPath $pipelineFallback -Force
    }

    Write-Host "Bundling ILSpyCmd $IlspyVersion..."
    $ilspyTool = Join-Path $WorkDirectory 'ilspy-tool'
    & dotnet tool install ilspycmd --tool-path $ilspyTool --version $IlspyVersion
    if ($LASTEXITCODE -ne 0) {
        throw "ILSpyCmd install failed with exit code $LASTEXITCODE."
    }
    $ilspyDll = Get-ChildItem -Path $ilspyTool -Recurse -File -Filter 'ilspycmd.dll' | Select-Object -First 1
    if (-not $ilspyDll) {
        throw 'Could not locate ilspycmd.dll after tool installation.'
    }
    Copy-Item -Path (Join-Path $ilspyDll.Directory.FullName '*') -Destination $ilspyOut -Recurse -Force

    Write-Host "Bundling .NET runtime $DotnetRuntimeVersion win-x64..."
    $dotnetInstall = Join-Path $WorkDirectory 'dotnet-install.ps1'
    Invoke-WebRequest -Uri $DotnetInstallUrl -OutFile $dotnetInstall
    & $dotnetInstall -Runtime dotnet -Version $DotnetRuntimeVersion -Architecture x64 -InstallDir $runtime -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install failed with exit code $LASTEXITCODE."
    }

    Copy-Item (Join-Path $scriptRoot 'Invoke-Offline.ps1') (Join-Path $stage 'Run-TerrariaDecompiler.ps1') -Force
    Copy-Item (Join-Path $scriptRoot 'Audit-Offline.ps1') (Join-Path $stage 'Audit-Offline.ps1') -Force

    $launcher = @'
@echo off
setlocal
set "INPUT=%~1"
if "%INPUT%"=="" set "INPUT=C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe"
if not exist "%INPUT%" (
  echo Terraria input not found:
  echo   %INPUT%
  echo.
  echo Drag Terraria.exe or the Terraria install folder onto RUN-DECOMPILER.cmd.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-TerrariaDecompiler.ps1" -TerrariaInput "%INPUT%" -OutputDirectory "%~dp0output"
set "ERR=%ERRORLEVEL%"
echo.
if not "%ERR%"=="0" echo Decompiler failed with exit code %ERR%.
if "%ERR%"=="0" echo Done. Open the output folder.
pause
exit /b %ERR%
'@
    Set-Content -Path (Join-Path $stage 'RUN-DECOMPILER.cmd') -Value $launcher -Encoding ASCII

    $readme = @"
Terraria Decompiler - OFFLINE Windows x64 bundle

NO RUNTIME DOWNLOADS.
This folder already contains:
- ILSpyCmd $IlspyVersion
- .NET runtime $DotnetRuntimeVersion
- Microsoft .NET Framework 4.0 reference assemblies
- Redistributable Microsoft XNA Framework 4.0 Refresh runtime references

Terraria's own install directory is the FIRST reference source.
The runner scans DLLs sitting next to Terraria.exe, keeps the managed .NET assemblies, and ignores native DLLs for ILSpy reference resolution. This lets it use the genuine Microsoft.Xna.Framework.Content.Pipeline.dll and any other managed dependencies Re-Logic ships with the game.

EASIEST USE:
1. Drag Terraria.exe (or the Terraria install folder) onto RUN-DECOMPILER.cmd
2. Wait for both ILSpy passes and the audit
3. Open output\

Default Steam install path is used if you double-click RUN-DECOMPILER.cmd with no argument.

The output ZIP is named TerrariaDecomp-<detected-version>-clean.zip.
The audit is output\audit\audit.md.
Reference provenance is output\audit\reference-sources.json.

The bundle itself does not contact the network. Future Terraria versions may introduce new dependencies; if the audit stops being zero, update/rebuild this bundle rather than hiding the diagnostic.
"@
    Set-Content -Path (Join-Path $stage 'README-OFFLINE.txt') -Value $readme -Encoding UTF8

    $notices = @'
Third-party component notes

ILSpy / ICSharpCode.Decompiler
- Project: https://github.com/icsharpcode/ILSpy
- License: MIT

Microsoft .NET runtime
- Bundled from Microsoft's official dotnet-install distribution.
- License and third-party notices are included inside the runtime directory where supplied by Microsoft.

Microsoft .NET Framework reference assemblies
- Package: Microsoft.NETFramework.ReferenceAssemblies.net40 1.0.3
- Distributed under the package's MIT license.

Microsoft XNA Framework Redistributable 4.0 Refresh
- Runtime assemblies are taken from Microsoft's official XNA Framework Redistributable.
- Microsoft describes these runtime libraries as redistributable with Windows products, subject to the XNA license terms.

Microsoft.Xna.Framework.Content.Pipeline.dll
- NOT included in this bundle.
- Current Terraria installations ship the genuine Microsoft assembly alongside Terraria.exe.
- At runtime the decompiler temporarily uses managed DLLs from the user's own Terraria installation as metadata references. Those DLLs are not added to the generated source ZIP or redistributed by this bundle.
'@
    Set-Content -Path (Join-Path $stage 'THIRD-PARTY-NOTICES.txt') -Value $notices -Encoding UTF8

    $manifest = Get-ChildItem -Path $stage -Recurse -File | ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($stage.Length).TrimStart('\')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $stage 'bundle-manifest.json') -Encoding UTF8

    Write-Host 'Smoke-testing bundled ILSpy + local runtime...'
    $bundledIlspy = Get-ChildItem -Path $ilspyOut -Recurse -File -Filter 'ilspycmd.dll' | Select-Object -First 1
    & (Join-Path $runtime 'dotnet.exe') $bundledIlspy.FullName --version
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled ILSpy smoke test failed with exit code $LASTEXITCODE."
    }

    $zipPath = Join-Path $OutputDirectory ($bundleName + '.zip')
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Offline bundle: $zipPath"
    Get-Item -LiteralPath $zipPath
}
finally {
    if (Test-Path -LiteralPath $WorkDirectory) {
        Remove-Item -LiteralPath $WorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

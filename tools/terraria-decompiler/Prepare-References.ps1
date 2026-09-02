[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$XnaInstaller = '',

    [string]$WorkDirectory = '',

    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$XnaDownloadUrl = 'https://download.microsoft.com/download/E/C/6/EC68782D-872A-4D58-A8D3-87881995CDD4/XNAGS40_setup.exe'
$Net40PackageUrl = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net40/1.0.3/microsoft.netframework.referenceassemblies.net40.1.0.3.nupkg'
$KnownXnaSha256 = 'e905f67edefb228ebb58277f8d24e1ec3460ead5d0ab57bd544246cb4465154b'

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Invoke-MsiAdminExtract {
    param(
        [Parameter(Mandatory = $true)][string]$MsiPath,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    New-Item -ItemType Directory -Force -Path $TargetDirectory | Out-Null
    Write-Host "Administrative-extracting $(Split-Path $MsiPath -Leaf)..."
    & msiexec.exe /a $MsiPath /qn /norestart "TARGETDIR=$TargetDirectory"
    $code = $LASTEXITCODE
    if ($code -notin @(0, 3010)) {
        throw "msiexec /a failed for '$MsiPath' with exit code $code."
    }
}

function Extract-SfxCab {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [Parameter(Mandatory = $true)][string]$CabPath
    )

    if (-not ('Gloader.TerrariaTools.SfxCab' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;

namespace Gloader.TerrariaTools {
    public static class SfxCab {
        public static long ExtractLargestCab(string exePath, string cabPath) {
            byte[] data = File.ReadAllBytes(exePath);
            int bestOffset = -1;
            uint bestLength = 0;

            for (int i = 0; i <= data.Length - 36; i++) {
                if (data[i] != (byte)'M' || data[i + 1] != (byte)'S' || data[i + 2] != (byte)'C' || data[i + 3] != (byte)'F')
                    continue;

                uint cbCabinet = BitConverter.ToUInt32(data, i + 8);
                ushort folderCount = BitConverter.ToUInt16(data, i + 26);
                ushort fileCount = BitConverter.ToUInt16(data, i + 28);

                if (cbCabinet < 36 || folderCount == 0 || fileCount == 0)
                    continue;
                if ((long)i + cbCabinet > data.LongLength)
                    continue;

                if (cbCabinet > bestLength) {
                    bestOffset = i;
                    bestLength = cbCabinet;
                }
            }

            if (bestOffset < 0)
                throw new InvalidDataException("No valid embedded Microsoft Cabinet payload was found.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cabPath))!);
            using (var source = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(cabPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                source.Position = bestOffset;
                byte[] buffer = new byte[1024 * 1024];
                long remaining = bestLength;
                while (remaining > 0) {
                    int want = (int)Math.Min(buffer.Length, remaining);
                    int read = source.Read(buffer, 0, want);
                    if (read <= 0)
                        throw new EndOfStreamException();
                    target.Write(buffer, 0, read);
                    remaining -= read;
                }
            }

            return bestLength;
        }
    }
}
'@
    }

    $length = [Gloader.TerrariaTools.SfxCab]::ExtractLargestCab($ExePath, $CabPath)
    Write-Host "Recovered embedded CAB payload: $length bytes"
}

$OutputDirectory = Resolve-FullPath $OutputDirectory
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("gloader-terraria-refs-" + [guid]::NewGuid().ToString('N'))
}
$WorkDirectory = Resolve-FullPath $WorkDirectory

New-Item -ItemType Directory -Force -Path $OutputDirectory, $WorkDirectory | Out-Null

try {
    # .NET Framework 4.0 reference assemblies. These remove the last WinForms/System.Drawing
    # resolution scars that remain when ILSpy runs only against modern .NET runtime assemblies.
    $netPkg = Join-Path $WorkDirectory 'net40.nupkg'
    $netZip = Join-Path $WorkDirectory 'net40.zip'
    $netExtract = Join-Path $WorkDirectory 'net40'

    Write-Host 'Downloading Microsoft .NET Framework 4.0 reference assemblies...'
    Invoke-WebRequest -Uri $Net40PackageUrl -OutFile $netPkg
    Copy-Item $netPkg $netZip -Force
    Expand-Archive -Path $netZip -DestinationPath $netExtract -Force

    $mscorlib = Get-ChildItem -Path $netExtract -Recurse -File -Filter 'mscorlib.dll' |
        Where-Object { $_.FullName -match '[\\/]v4\.0[\\/]' } |
        Select-Object -First 1
    if (-not $mscorlib) {
        throw 'Could not locate the .NET Framework 4.0 reference-assembly directory in the NuGet package.'
    }

    Get-ChildItem -Path $mscorlib.Directory.FullName -File -Filter '*.dll' |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $OutputDirectory $_.Name) -Force }

    # XNA Game Studio 4.0 Refresh contains the exact XNA assemblies Terraria 1.4.x references,
    # including Content.Pipeline, which is not part of the runtime-only redistributable.
    if ([string]::IsNullOrWhiteSpace($XnaInstaller)) {
        $XnaInstaller = Join-Path $WorkDirectory 'XNAGS40_setup.exe'
        Write-Host 'Downloading Microsoft XNA Game Studio 4.0 Refresh...'
        Invoke-WebRequest -Uri $XnaDownloadUrl -OutFile $XnaInstaller
    }
    $XnaInstaller = Resolve-FullPath $XnaInstaller
    if (-not (Test-Path -LiteralPath $XnaInstaller -PathType Leaf)) {
        throw "XNA installer not found: $XnaInstaller"
    }

    $signature = Get-AuthenticodeSignature -FilePath $XnaInstaller
    $xnaHash = (Get-FileHash -LiteralPath $XnaInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    $validMicrosoftSignature = ($signature.Status -eq 'Valid' -and $signature.SignerCertificate -and $signature.SignerCertificate.Subject -match 'Microsoft')
    $knownExactHash = ($xnaHash -eq $KnownXnaSha256)
    if (-not $validMicrosoftSignature -and -not $knownExactHash) {
        throw "XNA installer verification failed. Authenticode status: $($signature.Status); SHA-256: $xnaHash"
    }
    Write-Host "XNA installer verified. SHA-256: $xnaHash"

    $xnaCab = Join-Path $WorkDirectory 'xna-bootstrap.cab'
    $xnaTop = Join-Path $WorkDirectory 'xna-top'
    Extract-SfxCab -ExePath $XnaInstaller -CabPath $xnaCab
    New-Item -ItemType Directory -Force -Path $xnaTop | Out-Null

    & "$env:SystemRoot\System32\expand.exe" '-F:*' $xnaCab $xnaTop | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "expand.exe failed to unpack the XNA bootstrap CAB with exit code $LASTEXITCODE."
    }

    $redistsMsi = Get-ChildItem -Path $xnaTop -Recurse -File -Filter 'redists.msi' | Select-Object -First 1
    if (-not $redistsMsi) {
        throw 'The XNA bootstrap payload did not contain redists.msi.'
    }

    $redistsAdmin = Join-Path $WorkDirectory 'xna-redists-admin'
    Invoke-MsiAdminExtract -MsiPath $redistsMsi.FullName -TargetDirectory $redistsAdmin

    $xnaFxMsi = Get-ChildItem -Path $redistsAdmin -Recurse -File -Filter 'xnafx40_redist.msi' | Select-Object -First 1
    $sharedMsi = Get-ChildItem -Path $redistsAdmin -Recurse -File -Filter 'xnags_shared.msi' | Select-Object -First 1
    if (-not $xnaFxMsi -or -not $sharedMsi) {
        $available = Get-ChildItem -Path $redistsAdmin -Recurse -File -Filter '*.msi' | ForEach-Object FullName
        throw "Could not locate xnafx40_redist.msi and xnags_shared.msi after extracting redists.msi. MSI files found:`n$($available -join "`n")"
    }

    $xnaFxAdmin = Join-Path $WorkDirectory 'xna-fx-admin'
    $xnaSharedAdmin = Join-Path $WorkDirectory 'xna-shared-admin'
    Invoke-MsiAdminExtract -MsiPath $xnaFxMsi.FullName -TargetDirectory $xnaFxAdmin
    Invoke-MsiAdminExtract -MsiPath $sharedMsi.FullName -TargetDirectory $xnaSharedAdmin

    $requiredXna = @(
        'Microsoft.Xna.Framework.dll',
        'Microsoft.Xna.Framework.Game.dll',
        'Microsoft.Xna.Framework.Graphics.dll',
        'Microsoft.Xna.Framework.Xact.dll',
        'Microsoft.Xna.Framework.Content.Pipeline.dll'
    )

    foreach ($name in $requiredXna) {
        $candidate = Get-ChildItem -Path $xnaFxAdmin, $xnaSharedAdmin -Recurse -File -Filter $name |
            Sort-Object Length -Descending |
            Select-Object -First 1
        if (-not $candidate) {
            throw "Required XNA assembly was not recovered: $name"
        }
        Copy-Item $candidate.FullName (Join-Path $OutputDirectory $name) -Force
    }

    $manifest = foreach ($file in Get-ChildItem -Path $OutputDirectory -File -Filter '*.dll' | Sort-Object Name) {
        [pscustomobject]@{
            name = $file.Name
            bytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $OutputDirectory 'references.json') -Encoding UTF8

    Write-Host "Prepared $($manifest.Count) reference assemblies in $OutputDirectory"
}
finally {
    if (-not $KeepWork -and (Test-Path -LiteralPath $WorkDirectory)) {
        Remove-Item -LiteralPath $WorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

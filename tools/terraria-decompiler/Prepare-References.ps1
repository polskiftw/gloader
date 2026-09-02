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

    $sevenZip = Get-Command '7z.exe' -ErrorAction SilentlyContinue
    if (-not $sevenZip) {
        $sevenZipPath = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (Test-Path -LiteralPath $sevenZipPath) { $sevenZip = Get-Item -LiteralPath $sevenZipPath }
    }
    if (-not $sevenZip) {
        throw '7-Zip is required to unpack the legacy XNA MSI/CAB payloads. Install 7-Zip or use the GitHub Actions runner.'
    }
    $sevenZipExe = if ($sevenZip.PSObject.Properties.Name -contains 'Source') { $sevenZip.Source } else { $sevenZip.FullName }

    & $sevenZipExe x $xnaCab "-o$xnaTop" -y | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to unpack the XNA bootstrap CAB with exit code $LASTEXITCODE."
    }

    $redistsMsi = Get-ChildItem -Path $xnaTop -Recurse -File -Filter 'redists.msi' | Select-Object -First 1
    if (-not $redistsMsi) {
        throw 'The XNA bootstrap payload did not contain redists.msi.'
    }

    $redistsUnpacked = Join-Path $WorkDirectory 'xna-redists-unpacked'
    New-Item -ItemType Directory -Force -Path $redistsUnpacked | Out-Null
    & $sevenZipExe x $redistsMsi.FullName "-o$redistsUnpacked" -y | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to unpack redists.msi with exit code $LASTEXITCODE."
    }

    $sharedInstaller = Get-ChildItem -Path $redistsUnpacked -Recurse -File |
        Where-Object { $_.Name -eq 'SharedFilesInstaller_File' -or $_.Name -match '^SharedFilesInstaller' } |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if (-not $sharedInstaller) {
        $available = Get-ChildItem -Path $redistsUnpacked -Recurse -File | Select-Object -First 50 -ExpandProperty FullName
        throw "Could not locate the XNA shared-components installer after unpacking redists.msi. Files found:`n$($available -join "`n")"
    }

    $sharedUnpacked = Join-Path $WorkDirectory 'xna-shared-unpacked'
    New-Item -ItemType Directory -Force -Path $sharedUnpacked | Out-Null
    & $sevenZipExe x $sharedInstaller.FullName "-o$sharedUnpacked" -y | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to unpack '$($sharedInstaller.Name)' with exit code $LASTEXITCODE."
    }

    $sharedCab = Get-ChildItem -Path $sharedUnpacked -Recurse -File |
        Where-Object {
            if ($_.Length -lt 36) { return $false }
            $stream = [System.IO.File]::OpenRead($_.FullName)
            try {
                $header = New-Object byte[] 4
                if ($stream.Read($header, 0, 4) -ne 4) { return $false }
                return ($header[0] -eq 0x4D -and $header[1] -eq 0x53 -and $header[2] -eq 0x43 -and $header[3] -eq 0x46)
            }
            finally {
                $stream.Dispose()
            }
        } |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if (-not $sharedCab) {
        $available = Get-ChildItem -Path $sharedUnpacked -Recurse -File | Select-Object -First 80 -ExpandProperty FullName
        throw "Could not locate the embedded Cabinet stream in the XNA shared-components MSI. Files found:`n$($available -join "`n")"
    }

    $sharedCabUnpacked = Join-Path $WorkDirectory 'xna-shared-cab-unpacked'
    New-Item -ItemType Directory -Force -Path $sharedCabUnpacked | Out-Null
    & $sevenZipExe x $sharedCab.FullName "-o$sharedCabUnpacked" -y | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to unpack the XNA shared-components Cabinet stream with exit code $LASTEXITCODE."
    }

    $requiredXna = @(
        'Microsoft.Xna.Framework.dll',
        'Microsoft.Xna.Framework.Game.dll',
        'Microsoft.Xna.Framework.Graphics.dll',
        'Microsoft.Xna.Framework.Xact.dll',
        'Microsoft.Xna.Framework.Content.Pipeline.dll'
    )

    foreach ($name in $requiredXna) {
        $logicalName = $name.Replace('.', '_')
        $candidate = Get-ChildItem -Path $sharedCabUnpacked -Recurse -File -Filter $logicalName |
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

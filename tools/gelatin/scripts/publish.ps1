$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ToolRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Project = Join-Path $ToolRoot "src\Gelatin.App\Gelatin.App.csproj"
$DistRoot = Join-Path $ToolRoot "dist"
$PublishDirectory = Join-Path $DistRoot "gelatin"
$Archive = Join-Path $DistRoot "gelatin-0.1.2-win-x64.zip"

$env:AVALONIA_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

New-Item $PublishDirectory -ItemType Directory -Force | Out-Null

dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $PublishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "Gelatin publish failed with exit code $LASTEXITCODE."
}

$Executable = Join-Path $PublishDirectory "Gelatin.exe"
if (-not (Test-Path $Executable -PathType Leaf)) {
    throw "Publish completed without the required Gelatin.exe."
}

Compress-Archive -Path (Join-Path $PublishDirectory "*") -DestinationPath $Archive -CompressionLevel Optimal -Force

if (-not (Test-Path $Archive -PathType Leaf)) {
    throw "Gelatin package archive was not created."
}

$Hash = (Get-FileHash -Path $Archive -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Published: $PublishDirectory"
Write-Host "Packaged:  $Archive"
Write-Host "SHA256:    $Hash"

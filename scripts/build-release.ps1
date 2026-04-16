<#
.SYNOPSIS
Builds, publishes, and packages the RDAT Copilot into an offline-ready ZIP.
.DESCRIPTION
This script performs a self-contained release publish tailored for Windows 10/11 x64,
incorporates native required binaries for DirectML + ONNX and LanceDB, and zips the payload.
#>

$ErrorActionPreference = "Stop"
$PublishDir = ".\src\RDAT.Copilot.App\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"
$ZipName = "RDAT-Copilot-Portable-v1.0.zip"
$SlnFilePath = ".\RDAT.Copilot.sln"

Write-Host "1. Building & Publishing RDAT Copilot (Release)..." -ForegroundColor Cyan

# Publish command utilizing strict win-x64 targeting and ReadyToRun optimization.
# -p:WindowsPackageType=None turns it into an unpackaged standard Win32 portable .exe.
# PublishTrimmed is disabled because WinUI 3 uses reflection heavily.
dotnet publish ".\src\RDAT.Copilot.App\RDAT.Copilot.App.csproj" `
    -c "Release" `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:WindowsPackageType=None

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Exiting." -ForegroundColor Red
    exit 1
}

Write-Host "2. Ensuring Native Binaries are injected..." -ForegroundColor Cyan

$NativeDlls = @("onnxruntime.dll", "onnxruntime_providers_shared.dll", "DirectML.dll")

foreach ($dll in $NativeDlls) {
    $destDll = Join-Path $PublishDir $dll
    if (-not (Test-Path $destDll)) {
        Write-Host "  -> Warning: $dll not found in publish directory." -ForegroundColor Yellow
    } else {
        Write-Host "  -> $dll verified in publish payload." -ForegroundColor Green
    }
}

Write-Host "3. Packaging into $ZipName..." -ForegroundColor Cyan
if (Test-Path $ZipName) {
    Remove-Item $ZipName -Force
}

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipName -Force
Write-Host "DONE! Portable build created at $PWD\$ZipName" -ForegroundColor Green

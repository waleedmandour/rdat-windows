<#
.SYNOPSIS
Builds, publishes, and packages the RDAT Copilot into an offline-ready ZIP.
.DESCRIPTION
This script performs a self-contained release publish tailored for Windows 10/11 x64,
incorporates native required binaries for DirectML + ONNX and LanceDB, and zips the payload.
#>

$ErrorActionPreference = "Stop"
$PublishDir = ".\publish"
$ZipName = "RDAT-Copilot-Portable-win-x64.zip"
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
    -p:WindowsPackageType=None `
    -p:Platform=x64 `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Exiting." -ForegroundColor Red
    exit 1
}

Write-Host "2. Ensuring Native Binaries are injected..." -ForegroundColor Cyan

$NativeDlls = @("onnxruntime.dll", "onnxruntime_providers_shared.dll", "DirectML.dll")

foreach ($dll in $NativeDlls) {
    $found = Get-ChildItem -Path $PublishDir -Filter $dll -Recurse -File
    if ($found.Count -gt 0) {
        Write-Host "  -> $dll verified in publish payload." -ForegroundColor Green
    } else {
        # Check Windows System32 for DirectML.dll as fallback
        if ($dll -eq "DirectML.dll") {
            $systemDirectMl = Join-Path $env:SystemRoot "System32\DirectML.dll"
            if (Test-Path $systemDirectMl) {
                Copy-Item -Path $systemDirectMl -Destination (Join-Path $PublishDir "DirectML.dll") -Force
                Write-Host "  -> $dll copied from system." -ForegroundColor Green
                continue
            }
        }
        Write-Host "  -> Warning: $dll not found in publish directory." -ForegroundColor Yellow
    }
}

Write-Host "3. Packaging into $ZipName..." -ForegroundColor Cyan
if (Test-Path $ZipName) {
    Remove-Item $ZipName -Force
}

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipName -Force

$zipSize = (Get-Item $ZipName).Length / 1MB
Write-Host "DONE! Portable build created at $PWD\$ZipName" -ForegroundColor Green
Write-Host "Size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green

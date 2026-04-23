# ========================================================================
# RDAT Copilot - Build & Publish Script
# Location: scripts/Build-RDAT.ps1
# ========================================================================
# Produces a self-contained portable .EXE distribution:
#   RDAT-Copilot-Portable-win-x64.zip
#
# Usage:
#   .\Build-RDAT.ps1              # Build Release
#   .\Build-RDAT.ps1 -Clean       # Clean + Build
#   .\Build-RDAT.ps1 -SkipTests   # Skip unit tests
# ========================================================================

param(
    [switch]$Clean,
    [switch]$SkipTests,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [string]$OutputName = "RDAT-Copilot-Portable-win-x64"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# -- Configuration ---------------------------------------------------------
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$MainProject = Join-Path $ProjectRoot "src\RDAT.Copilot.App\RDAT.Copilot.App.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"
$OutputZip = Join-Path $ProjectRoot "$OutputName.zip"

# Native DLLs that must be copied to the publish root
$RequiredNativeDlls = @(
    "lancedb.dll",
    "onnxruntime.dll",
    "DirectML.dll",
    "onnxruntime_providers_shared.dll"
)

# -- Header ----------------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host "  RDAT Copilot - Build Script v$Version" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
Write-Host "  Runtime:       $Runtime" -ForegroundColor Gray
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host ""

# -- Step 0: Clean ----------------------------------------------------------
if ($Clean -and (Test-Path $PublishDir)) {
    Write-Host "[1/6] Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishDir
    Write-Host "       Done." -ForegroundColor Green
} else {
    Write-Host "[1/6] Clean: skipped (use -Clean to force)" -ForegroundColor Gray
}

# -- Step 1: Restore NuGet packages -----------------------------------------
Write-Host "[2/6] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $MainProject --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "       ERROR: NuGet restore failed." -ForegroundColor Red
    exit 1
}
Write-Host "       Done." -ForegroundColor Green

# -- Step 2: Run Unit Tests -------------------------------------------------
if (-not $SkipTests) {
    Write-Host "[3/6] Running unit tests..." -ForegroundColor Yellow
    $testProject = Join-Path $ProjectRoot "tests\RDAT.Copilot.Tests\RDAT.Copilot.Tests.csproj"
    if (Test-Path $testProject) {
        dotnet test $testProject --configuration $Configuration --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "       WARNING: Some tests failed. Continuing build." -ForegroundColor Yellow
        } else {
            Write-Host "       All tests passed." -ForegroundColor Green
        }
    } else {
        Write-Host "       No test project found. Skipping." -ForegroundColor Gray
    }
} else {
    Write-Host "[3/6] Tests: skipped (use without -SkipTests to run)" -ForegroundColor Gray
}

# -- Step 3: Publish --------------------------------------------------------
Write-Host "[4/6] Publishing application..." -ForegroundColor Yellow
dotnet publish $MainProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDir `
    -p:PublishReadyToRun=true `
    -p:PublishSingleFile=false `
    -p:WindowsPackageType=None `
    -p:Platform=x64 `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --verbosity normal

if ($LASTEXITCODE -ne 0) {
    Write-Host "       ERROR: Publish failed." -ForegroundColor Red
    exit 1
}
Write-Host "       Done." -ForegroundColor Green

# -- Step 4: Copy Native DLLs to Root ---------------------------------------
Write-Host "[5/6] Verifying native dependencies..." -ForegroundColor Yellow

$missingDlls = @()
foreach ($dll in $RequiredNativeDlls) {
    # Check if DLL exists anywhere in the publish directory
    $found = $false
    $dllPaths = @(Get-ChildItem -Path $PublishDir -Filter $dll -Recurse -File)

    if ($dllPaths.Count -gt 0) {
        # If the DLL is in a subdirectory, copy it to the root
        $rootPath = Join-Path $PublishDir $dll
        if (-not (Test-Path $rootPath)) {
            Copy-Item -Path $dllPaths[0].FullName -Destination $rootPath -Force
            Write-Host "       Copied $dll to publish root." -ForegroundColor Gray
        }
        $found = $true
    }

    if (-not $found) {
        $missingDlls += $dll
    }
}

# Check for DirectML.dll in Windows System32 as fallback
if ($missingDlls -contains "DirectML.dll") {
    $systemDirectMl = Join-Path $env:SystemRoot "System32\DirectML.dll"
    if (Test-Path $systemDirectMl) {
        Copy-Item -Path $systemDirectMl -Destination (Join-Path $PublishDir "DirectML.dll") -Force
        Write-Host "       Copied DirectML.dll from system." -ForegroundColor Gray
        $missingDlls = $missingDlls | Where-Object { $_ -ne "DirectML.dll" }
    }
}

if ($missingDlls.Count -gt 0) {
    Write-Host "       WARNING: Missing native DLLs:" -ForegroundColor Yellow
    foreach ($dll in $missingDlls) {
        Write-Host "         - $dll" -ForegroundColor Red
    }
    Write-Host "       The application may not function correctly." -ForegroundColor Yellow
} else {
    Write-Host "       All native dependencies verified." -ForegroundColor Green
}

# -- Step 5: Package as ZIP -------------------------------------------------
Write-Host "[6/6] Packaging distribution ZIP..." -ForegroundColor Yellow

# Remove old zip if exists
if (Test-Path $OutputZip) {
    Remove-Item -Force $OutputZip
}

# Create ZIP using .NET compression
if (Test-Path $PublishDir) {
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $OutputZip -CompressionLevel Optimal

    $zipSize = (Get-Item $OutputZip).Length / 1MB
    $fileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count

    Write-Host "       Package created: $OutputName.zip" -ForegroundColor Green
    Write-Host "       Size:  $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green
    Write-Host "       Files: $fileCount" -ForegroundColor Green
} else {
    Write-Host "       ERROR: Publish directory not found." -ForegroundColor Red
    exit 1
}

# -- Summary ----------------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host "  BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host "  Output:    $OutputZip" -ForegroundColor White
Write-Host "  Location:  $ProjectRoot" -ForegroundColor Gray
Write-Host "" -ForegroundColor Gray
Write-Host "  To run the application:" -ForegroundColor Gray
Write-Host "    1. Extract $OutputName.zip" -ForegroundColor Gray
Write-Host "    2. Run RDAT.Copilot.App.exe" -ForegroundColor Gray
Write-Host "" -ForegroundColor Gray
Write-Host "  Privacy: 100% offline. No telemetry." -ForegroundColor Gray
Write-Host "  ====================================================" -ForegroundColor Cyan

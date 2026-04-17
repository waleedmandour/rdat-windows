# ============================================================================
# NUCLEAR BUILD SCRIPT FOR RDAT COPILOT
# Purpose: Completely purge build artifacts and NuGet cache, then rebuild
# Usage: .\Nuclear-Build.ps1 -Configuration Debug|Release -Verbose
# ============================================================================

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

# ============================================================================
# STEP 1: Terminate All Build Processes
# ============================================================================
Write-Host "🔴 [STEP 1] Terminating build processes..." -ForegroundColor Yellow

$processNames = @("dotnet", "msbuild", "XamlCompiler", "VBCSCompiler", "conhost")
foreach ($processName in $processNames) {
    $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($processes) {
        Write-Verbose "  Killing process: $processName"
        $processes | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
        Write-Host "  ✓ Terminated $($processes.Count) $processName process(es)" -ForegroundColor Green
    }
}

# Wait for processes to fully terminate
Start-Sleep -Seconds 2

# ============================================================================
# STEP 2: Delete All bin and obj Folders Recursively
# ============================================================================
Write-Host "🔴 [STEP 2] Purging build artifacts (bin/obj folders)..." -ForegroundColor Yellow

$binObjPaths = @(
    "src/RDAT.Copilot.App/bin",
    "src/RDAT.Copilot.App/obj",
    "src/RDAT.Copilot.Core/bin",
    "src/RDAT.Copilot.Core/obj",
    "src/RDAT.Copilot.Infrastructure/bin",
    "src/RDAT.Copilot.Infrastructure/obj"
)

foreach ($path in $binObjPaths) {
    $fullPath = Join-Path (Get-Location) $path
    if (Test-Path $fullPath) {
        Write-Verbose "  Removing: $fullPath"
        Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Deleted: $path" -ForegroundColor Green
    }
}

# ============================================================================
# STEP 3: Clear Global NuGet Cache
# ============================================================================
Write-Host "🔴 [STEP 3] Clearing global NuGet cache..." -ForegroundColor Yellow

try {
    dotnet nuget locals all --clear
    Write-Host "  ✓ NuGet cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  ⚠️  NuGet cache clear failed (non-critical): $_" -ForegroundColor Yellow
}

# ============================================================================
# STEP 4: NuGet Restore
# ============================================================================
Write-Host "🔴 [STEP 4] Restoring NuGet packages..." -ForegroundColor Yellow

$projectPath = "src/RDAT.Copilot.App/RDAT.Copilot.App.csproj"
Write-Verbose "  Restoring: $projectPath"

try {
    dotnet restore $projectPath --verbosity minimal
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ NuGet restore completed successfully" -ForegroundColor Green
    } else {
        Write-Host "  ❌ NuGet restore failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "  ❌ NuGet restore error: $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# STEP 5: Clean Build
# ============================================================================
Write-Host "🔴 [STEP 5] Performing clean build in $Configuration mode..." -ForegroundColor Yellow

$buildArgs = @(
    "build",
    $projectPath,
    "-c", $Configuration,
    "-f", "net8.0-windows10.0.19041.0",
    "--runtime", "win-x64",
    "--no-restore",
    "-v", "detailed"
)

Write-Verbose "  Build command: dotnet $($buildArgs -join ' ')"

try {
    & dotnet $buildArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Build completed successfully" -ForegroundColor Green
        Write-Host ""
        Write-Host "✅ NUCLEAR BUILD SUCCESSFUL!" -ForegroundColor Cyan
        Write-Host "   Output path: src/RDAT.Copilot.App/bin/$Configuration/net8.0-windows10.0.19041.0/win-x64/" -ForegroundColor Cyan
    } else {
        Write-Host "  ❌ Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "  ❌ Build error: $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# SUMMARY
# ============================================================================
Write-Host ""
Write-Host "Build Summary:" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "  Framework: net8.0-windows10.0.19041.0" -ForegroundColor Cyan
Write-Host "  Runtime: win-x64" -ForegroundColor Cyan
Write-Host "  Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan

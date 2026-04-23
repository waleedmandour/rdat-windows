param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RDAT Copilot - Clean Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Terminate processes
Write-Host "[1/5] Terminating build processes..." -ForegroundColor Yellow
Get-Process -Name dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name msbuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name XamlCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Write-Host "      Done!" -ForegroundColor Green
Write-Host ""

# Step 2: Clean bin/obj
Write-Host "[2/5] Cleaning build artifacts..." -ForegroundColor Yellow
Remove-Item "src/RDAT.Copilot.App/bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "src/RDAT.Copilot.App/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "src/RDAT.Copilot.Core/bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "src/RDAT.Copilot.Core/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "src/RDAT.Copilot.Infrastructure/bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "src/RDAT.Copilot.Infrastructure/obj" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "      Done!" -ForegroundColor Green
Write-Host ""

# Step 3: Clear NuGet cache
Write-Host "[3/5] Clearing NuGet cache..." -ForegroundColor Yellow
dotnet nuget locals all --clear
Write-Host "      Done!" -ForegroundColor Green
Write-Host ""

# Step 4: Restore
Write-Host "[4/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore RDAT.Copilot.sln --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "      Done!" -ForegroundColor Green
Write-Host ""

# Step 5: Build
Write-Host "[5/5] Building application ($Configuration)..." -ForegroundColor Yellow
dotnet build RDAT.Copilot.sln `
    -c $Configuration `
    -p:Platform=x64 `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

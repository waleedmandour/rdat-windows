# Current Build Configuration State

**Generated:** April 18, 2026  
**Purpose:** Quick reference for current project configuration changes

---

## Current RDAT.Copilot.App.csproj Configuration

```xml
<PropertyGroup>
  <!-- ============ CORE FRAMEWORK ============ -->
  <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  <OutputType>WinExe</OutputType>
  <RootNamespace>RDAT.Copilot.App</RootNamespace>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <Platforms>x64</Platforms>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <UseWinUI>true</UseWinUI>
  
  <!-- ============ HARD-FIX: Output Path Normalization ============ -->
  <!-- CRITICAL: Prevents double-backslash bug in XamlCompiler -->
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  <!-- Explicitly set output paths WITHOUT trailing backslash -->
  <OutputPath>bin\$(Configuration)</OutputPath>
  <IntermediateOutputPath>obj\$(Configuration)</IntermediateOutputPath>
  <XamlGeneratedOutputPath>obj\$(Configuration)</XamlGeneratedOutputPath>
  
  <!-- ============ XAML Settings ============ -->
  <XamlCompilerSkipValidation>true</XamlCompilerSkipValidation>
  <EnableTypeInfoReflection>false</EnableTypeInfoReflection>
  
  <!-- ============ Packaging ============ -->
  <WindowsPackageType>None</WindowsPackageType>
  <EnableMsixTooling>false</EnableMsixTooling>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250108002" />
  <!-- HARD-FIX: Pinned BuildTools to match installed SDK version -->
  <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000" />
  <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
  <PackageReference Include="DocumentFormat.OpenXml" Version="3.2.0" />
  <PackageReference Include="System.Reactive" Version="6.0.1" />
</ItemGroup>
```

---

## Current Directory.Build.props Configuration

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <Platforms>x64</Platforms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    
    <!-- HARD-FIX: Disable buggy XamlCompiler in WinUI 3.1.6 -->
    <DisableXamlCompilation>true</DisableXamlCompilation>
    <XamlCompilerSkipValidation>true</XamlCompilerSkipValidation>
  </PropertyGroup>
  
  <!-- Override XAML compilation to skip the broken compiler -->
  <Target Name="CompileXaml" BeforeTargets="CoreCompile">
    <Message Text="INFO: XamlCompiler disabled due to MSB3073 path bug in Windows App SDK 1.6" Importance="normal" />
  </Target>
</Project>
```

---

## How to Revert Fixes When Upgrading SDK

### When Testing a New Windows App SDK Version:

1. **Temporarily remove from Directory.Build.props:**
   ```xml
   <!-- Comment out these lines -->
   <!-- <DisableXamlCompilation>true</DisableXamlCompilation> -->
   <!-- <XamlCompilerSkipValidation>true</XamlCompilerSkipValidation> -->
   ```

2. **Test build with new SDK:**
   ```powershell
   dotnet clean
   dotnet restore
   dotnet build -c Debug
   ```

3. **If build succeeds:** 
   - Remove ALL workaround properties from both files
   - Update this document with new SDK version
   - Commit to version control

4. **If build fails with same error:**
   - Restore workaround properties
   - Try different SDK version
   - Report results to Microsoft

---

## Key Properties to Monitor During Upgrades

| Property | Current Value | Why It's Important |
|----------|---------------|-------------------|
| `OutputPath` | `bin\$(Configuration)` | Must NOT have trailing backslash |
| `IntermediateOutputPath` | `obj\$(Configuration)` | Must NOT have trailing backslash |
| `XamlGeneratedOutputPath` | `obj\$(Configuration)` | Controls where XamlCompiler outputs files |
| `AppendRuntimeIdentifierToOutputPath` | `false` | Prevents win-x64 from being appended to paths |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.28000` | MUST match installed Windows SDK version |

---

## Testing After SDK Upgrade

Run this script to verify the build works:

```powershell
# Full clean build test
cd C:\RDAT\rdat-windows
Write-Host "Cleaning..." -ForegroundColor Cyan
dotnet clean --nologo -q

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore --nologo -q

Write-Host "Building..." -ForegroundColor Cyan
$buildResult = dotnet build -c Debug --nologo -v m 2>&1

Write-Host "Checking for errors..." -ForegroundColor Cyan
$errors = $buildResult | Select-String -Pattern "error"

if ($errors) {
    Write-Host "Build FAILED with errors:" -ForegroundColor Red
    $errors | Write-Host -ForegroundColor Red
} else {
    Write-Host "Build SUCCEEDED!" -ForegroundColor Green
    # Check if .exe was created
    $exePath = "src/RDAT.Copilot.App/bin/Debug/RDAT.Copilot.App.exe"
    if (Test-Path $exePath) {
        Write-Host "✓ Executable created: $exePath" -ForegroundColor Green
    }
}
```

---

## Known Issues & Workarounds

### Issue: XamlCompiler.exe exits with code 1
- **Workaround:** DisableXamlCompilation properties disable the compiler
- **Status:** BLOCKING - prevents .exe generation
- **Tracking:** Windows App SDK 1.6.250108002 XamlCompiler bug
- **Expected Resolution:** Windows App SDK 1.7.x (awaited)

### Issue: Double-backslash in paths (RESOLVED)
- **Cause:** OutputPath with trailing backslash + path concatenation
- **Fix:** Removed trailing backslashes from all output paths
- **Status:** ✅ FIXED

### Issue: BuildTools version mismatch
- **Cause:** NuGet resolving to 10.0.28000.1721 instead of 10.0.28000
- **Fix:** Explicitly pinned to 10.0.28000
- **Status:** ✅ FIXED

---

## Version Compatibility Matrix

| Component | Version | Compatibility | Notes |
|-----------|---------|---|---|
| .NET SDK | 8.0.420 | ✅ Tested | Primary target |
| Windows App SDK | 1.6.250108002 | ⚠️ Broken XamlCompiler | Need upgrade |
| Windows SDK | 10.0.28000 | ✅ Tested | Pinned version |
| BuildTools | 10.0.28000 | ✅ Tested | Matches Windows SDK |
| Visual Studio | 2022+ | ✅ Tested | Not required for CLI |

---

## Debugging XamlCompiler Issues

### To Enable Verbose Output:
```powershell
dotnet build -c Debug -v diag 2>&1 | Tee-Object -FilePath build_verbose.log
```

### To See XamlCompiler Input/Output:
```powershell
# Check input file
Get-Content "src/RDAT.Copilot.App/obj/Debug/input.json" | ConvertFrom-Json | Format-List

# Check if output was created
Test-Path "src/RDAT.Copilot.App/obj/Debug/output.json"
```

### To Run XamlCompiler Directly:
```powershell
$xamlCompiler = "C:\temp_nuget2\microsoft.windowsappsdk\1.6.250108002\buildTransitive\..\tools\net6.0\..\net472\XamlCompiler.exe"

& $xamlCompiler `
  "C:\RDAT\rdat-windows\src\RDAT.Copilot.App\obj\Debug\input.json" `
  "C:\RDAT\rdat-windows\src\RDAT.Copilot.App\obj\Debug\output.json"

# Check exit code
Write-Host "Exit Code: $LASTEXITCODE"
```

---

## Summary

✅ **Fixes Applied:** 4 major configuration changes  
❌ **Issues Remaining:** XamlCompiler.exe crash (Windows App SDK issue, not project)  
🔄 **Status:** Awaiting Windows App SDK upgrade for permanent fix  
📋 **Action Required:** Test new SDK versions when available

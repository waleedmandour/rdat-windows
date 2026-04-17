# RDAT Copilot Build Fixes - Windows App SDK 1.6.250108002 Workarounds

**Last Updated:** April 18, 2026  
**Status:** Critical issue blocking .exe generation - XamlCompiler.exe crash  
**Severity:** Blocks production builds  
**SDK Version:** Windows App SDK 1.6.250108002  
**.NET Version:** 8.0.420

---

## Executive Summary

The project encounters an MSB3073 error during the XAML compilation phase caused by XamlCompiler.exe (Windows App SDK 1.6.250108002) crashing with exit code 1. All fixes have been applied to the project configuration, but the root cause remains in the Windows App SDK itself. The workarounds documented here prepare the codebase for future SDK upgrades while documenting what we've tried.

---

## Fixes Applied ✅

### 1. Output Path Normalization

**File:** `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj`

**Issue:** XamlCompiler was concatenating paths with trailing backslashes, creating malformed paths like `obj\Debug\\input.json` (double backslash).

**Fix Applied:**
```xml
<!-- CRITICAL FIX: Paths must NOT have trailing backslash to prevent XamlCompiler path concatenation bug -->
<OutputPath>bin\$(Configuration)</OutputPath>
<IntermediateOutputPath>obj\$(Configuration)</IntermediateOutputPath>
<!-- Set XamlGeneratedOutputPath without trailing backslash to fix path construction -->
<XamlGeneratedOutputPath>obj\$(Configuration)</XamlGeneratedOutputPath>
```

**What This Does:**
- Removes trailing backslashes from build output paths
- Prevents MSBuild from double-concatenating path separators
- Ensures XamlCompiler receives correctly formatted paths

**Result:** The path is now correctly formatted as `obj\Debug\input.json` instead of `obj\Debug\\input.json`

---

### 2. Runtime Identifier Path Handling

**File:** `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj`

**Issue:** Even with correct paths, `win-x64` runtime identifier was being appended to paths when it shouldn't be.

**Fix Applied:**
```xml
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
```

**What This Does:**
- Prevents automatic appending of runtime identifiers to output paths
- Gives explicit control over path construction
- Avoids nested path structures that can confuse XAML compiler

---

### 3. Windows SDK BuildTools Version Pinning

**File:** `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj`

**Issue:** Project specified generic BuildTools version; NuGet resolved to 10.0.28000.1721 instead of 10.0.28000 (installed SDK version).

**Fix Applied:**
```xml
<PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000" />
```

**What This Does:**
- Explicitly pins BuildTools to match installed Windows SDK version
- Prevents NuGet version mismatch warnings
- Ensures consistent MSBuild targets across builds

---

### 4. Directory.Build.props Global Configuration

**File:** `Directory.Build.props` (workspace root)

**Changes Made:**
```xml
<PropertyGroup>
  <!-- ... existing properties ... -->
  
  <!-- HARD-FIX: Disable buggy XamlCompiler in WinUI 3.1.6 with path normalization issues -->
  <DisableXamlCompilation>true</DisableXamlCompilation>
  <XamlCompilerSkipValidation>true</XamlCompilerSkipValidation>
</PropertyGroup>

<!-- Override XAML compilation to skip the broken compiler -->
<Target Name="CompileXaml" BeforeTargets="CoreCompile">
  <Message Text="INFO: XamlCompiler disabled due to MSB3073 path bug in Windows App SDK 1.6" Importance="normal" />
</Target>
```

**Purpose:** Global configuration to disable problematic XAML compilation targets across all projects.

---

## Issues Attempted But Unsuccessful ❌

### Attempted Fix 1: Custom Target Overrides
**Method:** Added empty targets to override XamlCompiler tasks  
**Result:** Targets still ran from NuGet packages; override didn't prevent execution  
**Conclusion:** NuGet targets take precedence over project-level overrides

### Attempted Fix 2: Disabling UseWinUI
**Method:** Set `<UseWinUI>false</UseWinUI>`  
**Result:** Targets still imported and XamlCompiler still executed  
**Conclusion:** Windows App SDK always includes XAML compilation regardless of UseWinUI setting

### Attempted Fix 3: Patching NuGet Targets File
**Method:** Modified `Microsoft.UI.Xaml.Markup.Compiler.interop.targets` to disable Exec commands  
**Result:** OutputDeserializer failed trying to read non-existent output.json  
**Conclusion:** Cannot selectively disable parts of the pipeline; all-or-nothing approach needed

### Attempted Fix 4: Direct XamlCompiler.exe Invocation
**Method:** Ran XamlCompiler.exe directly with valid input.json  
**Result:** Exit code 1 with no error output to stderr  
**Conclusion:** XamlCompiler has internal issue; input/output files not the problem

---

## Root Cause Analysis

**The Problem:** `XamlCompiler.exe` (Windows App SDK 1.6.250108002) exits with code 1 when compiling XAML files for WinUI 3 applications targeting .NET 8.

**Contributing Factors:**
1. XamlCompiler is a .NET Framework 4.7.2 application
2. Input/output paths are correctly formatted
3. XAML files are syntactically valid (verified during previous fixes)
4. C# code compiles without errors
5. Only XAML compilation stage fails

**Likely Causes:**
- Incompatibility between Windows App SDK 1.6.250108002 and Windows SDK 10.0.28000
- Missing .NET Framework 4.7.2 dependencies on the build machine
- Corrupted XamlCompiler.exe binary in the NuGet package

---

## Verification Steps

### Step 1: Confirm Path Fix
```powershell
# Check that output paths don't have trailing backslashes
Get-Content "src/RDAT.Copilot.App/RDAT.Copilot.App.csproj" | Select-String "OutputPath|IntermediateOutputPath" | Select-Object -First 2
```

Expected output should show paths WITHOUT trailing backslashes:
```
OutputPath>bin\$(Configuration)</OutputPath>
IntermediateOutputPath>obj\$(Configuration)</IntermediateOutputPath>
```

### Step 2: Build and Check for Errors
```powershell
dotnet build src/RDAT.Copilot.App/RDAT.Copilot.App.csproj -c Debug 2>&1 | Where-Object {$_ -like "*error MSB3073*"}
```

If this command returns a line with MSB3073, the XamlCompiler issue persists.

### Step 3: Inspect Build Artifacts
```powershell
# Check if any artifacts were created before the crash
Get-ChildItem "src/RDAT.Copilot.App/bin/Debug" -Recurse -Name
```

---

## Recommended Migration Path

### Option 1: Downgrade Windows App SDK (Recommended Short-term)
1. Edit `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj`
2. Change: `<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.x" />`
3. Test with versions 1.5.3, 1.5.2, or 1.4.x
4. Rebuild: `dotnet clean && dotnet restore && dotnet build`

**Pros:**
- Quick fix if older versions don't have the bug
- Maintains all current features

**Cons:**
- May miss security updates
- Not a permanent solution

### Option 2: Install .NET Framework 4.7.2 (Try First)
1. Download .NET Framework 4.7.2 from Microsoft
2. Install on build machine
3. Restart build
4. Report results

**Pros:**
- May resolve missing dependencies for XamlCompiler.exe
- Costs only installation time

**Cons:**
- Adds build machine dependency
- Doesn't fix inherent XamlCompiler bug

### Option 3: Wait for Windows App SDK 1.7.x (Long-term)
1. Monitor Microsoft announcements for bug fixes
2. Set reminder to test with 1.7.x when released
3. Apply these fixes to a development branch for testing
4. Migrate production when 1.7.x is stable

**Pros:**
- Official fix from Microsoft
- All features and security patches included

**Cons:**
- Blocks current builds
- Timeline unknown

---

## Configuration Reference

### Critical Properties Set
| Property | Value | Purpose |
|----------|-------|---------|
| `TargetFramework` | net8.0-windows10.0.19041.0 | .NET 8 with Windows 10 2004+ support |
| `RuntimeIdentifier` | win-x64 | 64-bit Windows only |
| `OutputPath` | bin\$(Configuration) | No trailing backslash |
| `IntermediateOutputPath` | obj\$(Configuration) | No trailing backslash |
| `AppendTargetFrameworkToOutputPath` | false | Prevent path nesting |
| `AppendRuntimeIdentifierToOutputPath` | false | Prevent path nesting |
| `XamlGeneratedOutputPath` | obj\$(Configuration) | Explicit XAML output path |
| `DisableXamlCompilation` | true | Disable broken compiler |

### Package Versions
- Microsoft.WindowsAppSDK: 1.6.250108002
- Microsoft.Windows.SDK.BuildTools: 10.0.28000
- .NET: 8.0.420

---

## Build Commands for Testing

### Clean Build
```powershell
cd C:\RDAT\rdat-windows
dotnet clean
dotnet restore --packages "C:\temp_nuget"
dotnet build -c Debug -v m
```

### Rebuild with Logging
```powershell
dotnet build src/RDAT.Copilot.App/RDAT.Copilot.App.csproj -c Debug -v diag 2>&1 | Tee-Object build.log
```

### Check for XamlCompiler Errors
```powershell
Select-String -Path "build.log" -Pattern "XamlCompiler|MSB3073"
```

---

## Files Modified During Investigation

| File | Changes | Status |
|------|---------|--------|
| `Directory.Build.props` | Added DisableXamlCompilation properties | Applied ✅ |
| `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj` | Output path normalization, BuildTools pinning | Applied ✅ |
| `RDAT.Copilot.sln` | None | Unmodified |

---

## Troubleshooting Checklist

- [ ] Verified .NET 8.0.420 SDK is installed: `dotnet --version`
- [ ] Confirmed Windows SDK 10.0.28000 installation
- [ ] Checked .NET Framework 4.7.2 is available (for XamlCompiler)
- [ ] Cleared NuGet cache: `dotnet nuget locals all --clear`
- [ ] Restored packages fresh: `dotnet restore`
- [ ] Verified no projects are open in Visual Studio (can lock files)
- [ ] Checked disk space > 5 GB available
- [ ] Confirmed no antivirus blocking build processes
- [ ] Tested with `--packages` pointing to isolated NuGet folder

---

## Next Steps When Upgrading SDK

1. **Before upgrading:** Save this document and the current csproj configuration
2. **After upgrading:** Remove the `DisableXamlCompilation` properties from Directory.Build.props
3. **Test build:** Run `dotnet build -c Debug` and look for MSB3073 errors
4. **If errors persist:** Re-apply output path fixes documented above
5. **If no errors:** Report SDK version and configuration for team documentation

---

## Contact & Documentation

**Issue Tracking:** MSB3073 XamlCompiler.exe crash in Windows App SDK 1.6.250108002  
**Documentation:** This file  
**Last Tested:** April 18, 2026  
**SDK Tested:** 1.6.250108002  
**Outcome:** Workarounds applied, awaiting SDK upgrade for permanent fix

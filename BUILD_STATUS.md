# GitHub Actions Build Status

**Last Updated:** April 23, 2026

---

## Build Status: Fixed and Ready

### Previous Blocker (Resolved)
- Windows App SDK 1.6.250108002 contained a broken XamlCompiler.exe that crashed with exit code 1
- **Fix Applied:** Downgraded to Windows App SDK 1.5.3 which has a stable XamlCompiler
- **Additional Fixes:** Removed broken workarounds, added missing NuGet packages, fixed code-behind errors

---

## Changes Applied

### Critical Fixes
1. **Downgraded Windows App SDK** from 1.6.250108002 to 1.5.3 (XamlCompiler crash fix)
2. **Updated Windows SDK BuildTools** from 10.0.28000 to 10.0.22621
3. **Removed broken XAML workarounds** from Directory.Build.props (DisableXamlCompilation, CompileXaml override)
4. **Added missing NuGet packages:** Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging
5. **Added LanceDB NuGet package** for vector Translation Memory
6. **Added Google.Generative.AI package** for Gemini cloud fallback
7. **Fixed GlossaryPage** code-behind referencing non-existent DataGrid control
8. **Fixed GlossaryPage** XAML buttons missing Click event handlers
9. **Fixed EditorBridge** typo in "ghostTextResult" message type (was "ghosTextResult")
10. **Fixed GitHub Actions workflows** - correct solution path, added workload install
11. **Fixed System.Reactive version** consistency across all projects (6.0.1)
12. **Added explicit TargetFramework** to Core and Infrastructure projects
13. **Added default corpus data** (default-corpus-en-ar.json) matching RDAT-PWA
14. **Added Gemini cloud fallback service** for when local ONNX is unavailable

---

## Build Commands

### Quick Build
```powershell
dotnet restore RDAT.Copilot.sln
dotnet build RDAT.Copilot.sln -c Release -p:Platform=x64
```

### Build Portable .EXE
```powershell
dotnet publish src/RDAT.Copilot.App/RDAT.Copilot.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:WindowsPackageType=None -p:PublishReadyToRun=true `
    -p:Platform=x64 --output ./publish
```

### Full Build Script
```powershell
.\scripts\Build-RDAT.ps1 -Clean
```

---

## GitHub Actions Workflow

**File:** `.github/workflows/build-release.yml`

### Triggers:
- **Automatic:** Push to `main` branch with code changes
- **Manual:** GitHub Actions tab -> "Run workflow" button

### Workflow Steps:
1. Checkout code + LFS files
2. Setup .NET 8
3. Add MSBuild to PATH
4. Install WinUI 3 workloads
5. Restore NuGet packages
6. Build Release
7. Publish self-contained portable app
8. Verify native DLLs
9. Compress to .zip
10. Upload artifacts
11. Auto-create GitHub Release

---

## Build Output

```
When build succeeds:
├── RDAT-Copilot-Portable-win-x64.zip
│   ├── RDAT.Copilot.App.exe
│   ├── Dependencies (*.dll files)
│   ├── Assets/ (Monaco editor, corpus data)
│   └── Supporting files
├── GitHub Release (auto-created)
└── Artifacts (30-day retention)
```

---

## Included AI Models & Databases

### Local AI (ONNX Runtime + DirectML)
| Model | Purpose | Location |
|-------|---------|----------|
| Phi-3-mini-4k-instruct (DirectML INT4) | Ghost text generation | Models/phi3-mini-4k-instruct-onnx/ |
| all-MiniLM-L6-v2 | Semantic embeddings for TM | Models/minilm-l6-v2/ |

### Cloud AI (Optional)
| Service | Purpose | Configuration |
|---------|---------|---------------|
| Google Gemini 1.5 Flash | Cloud fallback translation | API key in Settings |

### Databases
| Database | Purpose | Implementation |
|----------|---------|----------------|
| LanceDB | Vector Translation Memory | In-memory + persistent |
| Default Corpus | EN-AR translation pairs | Assets/data/default-corpus-en-ar.json |

---

## Model Download

Before first use, download the ONNX models:
```powershell
.\scripts\download-models.ps1 -TargetFolder .\Models
```

This downloads:
- **MiniLM-L6-v2**: ~90MB embedding model
- **Phi-3-mini DirectML INT4**: ~2.4GB generative model

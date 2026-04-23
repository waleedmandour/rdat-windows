# GitHub Actions Build Status

**Last Updated:** April 24, 2026

---

## Build Status: Ready for CI Build

### Previous Blockers (All Resolved)
- Windows App SDK 1.6.250108002 XamlCompiler crash → Downgraded to 1.5.3
- Missing NuGet packages → Added CsWinRT, DirectML, logging providers
- Gemini 1.5 Flash → Updated to **Gemini 3.0 Flash** (current)
- Broken CI workload `maui-windows` → Updated to `maui-desktop`
- Missing DI registrations → Added HardwareService, StartupService
- LanceDbTmService stub methods → Implemented real embedding + cosine similarity
- Monaco editor HTML missing CDN fallback → Added dual-load (local + CDN)
- Ghost text JS used non-existent context key → Fixed with proper Tab/Escape handling
- GlossaryDisplayItem init-only mutation → Fixed with `with` expression

---

## Changes Applied (v1.1.0)

### Critical Fixes
1. **Updated Gemini endpoint** from `gemini-1.5-flash` to `gemini-3.0-flash`
2. **Updated Windows App SDK** to 1.5.240802000 (latest stable 1.5.x)
3. **Updated Windows SDK BuildTools** to 10.0.26100
4. **Added Microsoft.Windows.CsWinRT** 2.2.0 for C#/WinRT projection
5. **Added Microsoft.ML.OnnxRuntime.DirectML** 1.21.0 for GPU acceleration
6. **Updated OnnxRuntime** from 1.20.1 to 1.21.0
7. **Updated OnnxRuntimeGenAI** from 0.5.0 to 0.6.0
8. **Updated LanceDb** from 0.6.20 to 0.8.0
9. **Fixed CI workflows** — replaced `maui-windows` with `maui-desktop` workload
10. **Added `WindowsAppSDKSelfContained=true`** to publish command in CI
11. **Fixed LanceDbTmService** — real embedding generation with FNV-1a token hashing
12. **Fixed LanceDbTmService.CosineSimilarity** — actual dot-product computation
13. **Fixed GlossaryPage** — `init`-only `Direction` property mutated via `with` expression
14. **Registered HardwareService + StartupService** in DI container
15. **Added async startup initialization** in App.OnLaunched
16. **Fixed Monaco Editor HTML** — dual-load (local ms-appx + CDN fallback)
17. **Fixed monaco-ghost-text.js** — proper Tab/Escape key handling, debounce
18. **Fixed TranslationWorkspacePage** — ms-appx-web:// URI scheme, minimal editor fallback
19. **Added logging providers** (Console, Debug) for development diagnostics

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
    -p:WindowsAppSDKSelfContained=true `
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
- **Manual:** GitHub Actions tab → "Run workflow" button

### Workflow Steps:
1. Checkout code + LFS files
2. Setup .NET 8
3. Add MSBuild to PATH
4. Install WinUI 3 workloads (`maui-desktop`)
5. Restore NuGet packages
6. Build Release
7. Publish self-contained portable app
8. Verify native DLLs
9. Copy Assets to publish directory
10. Compress to .zip
11. Upload artifacts
12. Auto-create GitHub Release

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
| Google Gemini 3.0 Flash | Cloud fallback translation | API key in Settings |

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

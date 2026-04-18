# GitHub Actions Build Status & Workarounds

**Last Updated:** April 18, 2026

---

## 🚨 Current Build Status: XamlCompiler Blocker

### Error
```
MSB3073: The command "...XamlCompiler.exe" exited with code 1
```

### Root Cause
- Windows App SDK 1.6.250108002 contains broken XamlCompiler.exe
- Issue affects both local and GitHub Actions builds
- See: [XAML_COMPILER_WORKAROUNDS.md](XAML_COMPILER_WORKAROUNDS.md)

---

## ✅ Immediate Solution: SDK Downgrade

### Option 1: Downgrade to Windows App SDK 1.5.x (Recommended)

**Step 1:** Edit `src/RDAT.Copilot.App/RDAT.Copilot.App.csproj`
```xml
<!-- Change this: -->
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250108002" />

<!-- To this: -->
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.3" />

<!-- And update BuildTools: -->
<PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.22621" />
```

**Step 2:** Test locally
```powershell
cd C:\RDAT\rdat-windows
dotnet clean
dotnet restore
dotnet build "RDAT.Copilot.sln" -c Release -p:Platform=x64
```

**Step 3:** If successful, trigger GitHub Actions build
```powershell
git add .
git commit -m "Downgrade Windows App SDK to 1.5.3 (XamlCompiler fix)"
git push origin main
```

**Step 4:** GitHub Actions automatically triggers and builds

---

### Option 2: Await Windows App SDK 1.7.x Release

- Monitor: [Windows App SDK Releases](https://github.com/microsoft/WindowsAppSDK/releases)
- When 1.7.0 stable is released, update in .csproj and rebuild
- No code changes required—only dependency update

---

## 🤖 GitHub Actions Workflow Details

**File:** `.github/workflows/build-release.yml`

### Workflow Steps:
1. ✅ Checkout code + LFS files
2. ✅ Setup .NET 8
3. ✅ Restore NuGet packages
4. ⏳ Build Release (currently blocked by XamlCompiler)
5. 📦 Create output directory
6. 🗜️ Compress to .zip
7. 📤 Upload artifacts (available 30 days)
8. 🎯 Auto-create GitHub Release
9. ☁️ Ready for cloud distribution

### Triggers:
- **Automatic:** Push to `main` branch with code changes
- **Manual:** GitHub Actions tab → "Run workflow" button
- **Scheduled:** Can be configured for nightly builds

---

## 📊 Build Output Structure

```
When build succeeds, GitHub Actions creates:
│
├── RDAT.Copilot.App.zip (main artifact)
│   ├── RDAT.Copilot.App.exe
│   ├── Dependencies (*.dll files)
│   └── Supporting files
│
├── GitHub Release (auto-created)
│   └── Downloadable .zip file
│
└── Artifacts (30-day retention)
    └── Available in Actions tab
```

---

## 🚀 Quick Start When Build Works

### 1. **Monitor Build**
```
GitHub → Actions → "Build RDAT Copilot Release" → Latest run
```

### 2. **Download Artifact**
```
Actions → Run → Artifacts → rdat-copilot-windows → Download
```

### 3. **Download from Release**
```
Repository → Releases → Latest → Download RDAT.Copilot.App.zip
```

---

## 📦 Distribution Methods Ready

When build succeeds, distribute via:

| Method | Steps | Size Limit |
|--------|-------|-----------|
| **GitHub Releases** | Auto (included in workflow) | 2 GB per file |
| **Azure Blob Storage** | See DISTRIBUTION_GUIDE.md | Unlimited |
| **AWS S3** | See DISTRIBUTION_GUIDE.md | Unlimited |
| **Google Drive** | Manual upload | 15 GB free |
| **OneDrive** | Manual upload | 5 GB free |

---

## 🔧 Manual Workflow Trigger

If you don't want to push code:

1. GitHub → Actions tab
2. Click: "Build RDAT Copilot Release"
3. Click: "Run workflow"
4. Select: `main` branch
5. Click: "Run workflow"
6. Wait ~15-20 minutes

---

## 📝 Next Steps

### Immediate (Today)
- [ ] Test SDK downgrade locally (1.5.3)
- [ ] If successful: Push changes
- [ ] GitHub Actions auto-triggers build

### If SDK Downgrade Fails
- [ ] Try older version (1.4.x, 1.3.x)
- [ ] Or await Windows App SDK 1.7.x

### When Build Succeeds
- [ ] Download artifact from GitHub Actions
- [ ] Test executable locally
- [ ] Choose distribution method
- [ ] Publish to public

---

## 🧪 Testing Build Locally

```powershell
# Before GitHub Actions, test locally:
cd C:\RDAT\rdat-windows

# Clean previous build
dotnet clean

# Restore with updated SDK
dotnet restore

# Build Release
dotnet build -c Release -p:Platform=x64 --nologo

# Check output
Get-Item "src/RDAT.Copilot.App/bin/Release/**/RDAT.Copilot.App.exe"

# If .exe created successfully → GitHub Actions will work!
```

---

## 📞 Troubleshooting

### GitHub Actions Build Fails
1. Check: Build Logs in Actions tab
2. Usually same XamlCompiler issue
3. Solution: Downgrade SDK locally first, then push

### Can't Downgrade SDK
1. Remove: `Microsoft.WindowsAppSDK` reference
2. Try: WinUI 3 with different package source
3. Contact: Microsoft support / GitHub Issues

### Need Build on Different Machine
1. Push current code to GitHub
2. Use GitHub Actions (runs on Microsoft-hosted runners)
3. Download artifact from Actions

---

## 💡 Pro Tips

- **Set up branch protection:** Require passing builds before merge
- **Add build status badge:** Show build status in README
- **Schedule nightly builds:** Detect regressions early
- **Multi-platform builds:** Add separate workflow for x86, ARM64

---

## 🎯 Success Criteria

✅ Workflow file in `.github/workflows/build-release.yml`
✅ Solution file accessible at repository root
✅ Dependencies specified in project files
✅ Release artifacts auto-uploaded
✅ GitHub Release auto-created
⏳ XamlCompiler blocker resolved
✅ Distribution methods documented

---

**For detailed distribution methods, see:** [DISTRIBUTION_GUIDE.md](DISTRIBUTION_GUIDE.md)
**For XamlCompiler troubleshooting, see:** [XAML_COMPILER_WORKAROUNDS.md](XAML_COMPILER_WORKAROUNDS.md)

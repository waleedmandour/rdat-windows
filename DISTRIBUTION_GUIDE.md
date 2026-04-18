# RDAT Copilot - Distribution & Deployment Guide

**Last Updated:** April 18, 2026  
**App Size:** ~2+ GB (including ONNX models)

---

## 🏗️ Automated Build Pipeline

### GitHub Actions Workflow
- **Location:** `.github/workflows/build-release.yml`
- **Trigger:** Pushes to `main` branch or manual workflow_dispatch
- **Output:** Zipped executable with dependencies
- **Release:** Auto-creates GitHub Release with downloadable artifacts

**Build Status:** [View Workflows](https://github.com/waleedmandour/rdat-windows/actions)

---

## 📦 Distribution Methods (Ranked by Suitability)

### 1. **GitHub Releases** ⭐ Recommended for Initial Distribution
**Pros:**
- Free, integrated with your repository
- Versioning and changelog support
- Direct download links
- Can host multiple file formats

**Cons:**
- Single file limit: 2 GB per release (your app may exceed)
- Bandwidth limited for frequent downloads

**Setup:**
```bash
# Automated via GitHub Actions (already configured)
# Manual: Create release → Upload .zip file
```

**Estimate Time:** ~5 minutes per release

---

### 2. **Azure Blob Storage** ⭐⭐ Best for Large Files
**Pros:**
- Supports files up to 4.75 TB
- Cost-effective ($0.021/GB/month storage)
- Faster downloads globally
- Built-in versioning and access control

**Setup:**
```powershell
# Install Azure CLI
choco install azure-cli

# Authenticate
az login

# Create storage account
az storage account create `
  --name rdatcopilot `
  --resource-group rdat-resources `
  --location eastus

# Upload
az storage blob upload `
  --account-name rdatcopilot `
  --container-name releases `
  --name "RDAT.Copilot.v1.0.zip" `
  --file "RDAT.Copilot.App.zip"

# Generate SAS URL (shareable)
az storage blob generate-sas `
  --account-name rdatcopilot `
  --container-name releases `
  --name "RDAT.Copilot.v1.0.zip" `
  --permissions r `
  --expiry 2025-12-31
```

**Estimated Cost:** ~$1-5/month for 2GB+ storage

---

### 3. **AWS S3** ⭐⭐ Enterprise Solution
**Pros:**
- Unlimited file size
- CloudFront CDN integration (~$0.085/GB transferred)
- Cross-region replication
- Versioning and lifecycle policies

**Setup:**
```bash
# Install AWS CLI
choco install awscli

# Configure credentials
aws configure

# Create S3 bucket
aws s3api create-bucket \
  --bucket rdat-copilot-releases \
  --region us-east-1

# Upload with public access
aws s3 cp RDAT.Copilot.App.zip s3://rdat-copilot-releases/ \
  --acl public-read

# Get download URL
echo "https://rdat-copilot-releases.s3.amazonaws.com/RDAT.Copilot.App.zip"
```

**Estimated Cost:** ~$0.50-2/month for 2GB storage + transfer costs

---

### 4. **Google Drive / OneDrive** ✅ No Setup
**Pros:**
- Free (15 GB Google Drive, 5 GB OneDrive free tier)
- Easy sharing links
- No technical setup required

**Cons:**
- Download speed limits
- Not ideal for frequent updates
- Less version control

**How:**
1. Upload .zip to Google Drive
2. Right-click → Share → Get link
3. Share link with users

---

### 5. **MSIX Package & Windows Package Manager** ⭐⭐⭐ Professional
**Pros:**
- Single-click installation/updates
- Native Windows integration
- Users can install via `winget install rdat-copilot`
- Better versioning and rollback support

**Cons:**
- Requires code signing certificate (~$300/year)
- More complex setup

**Setup:**
```bash
# Create MSIX package from Release build
$publisherName = "CN=RDATCopilot"
$publisherDisplayName = "RDAT Copilot"

# Sign and package
dotnet publish src/RDAT.Copilot.App/RDAT.Copilot.App.csproj `
  -c Release `
  -p:Configuration=Release `
  -p:Platform=x64

# Create MSIX manifest and package
# (See MSIX_PACKAGING.md for detailed steps)
```

---

### 6. **Docker Container** (Alternative Runtime)
**Pros:**
- Consistent environment
- Easy deployment on servers
- Containerized with all dependencies

**Cons:**
- Not ideal for GUI desktop apps
- Requires Docker Desktop on client machine

---

## 🚀 Recommended Hybrid Approach

| Phase | Method | Size | Cost | Users |
|-------|--------|------|------|-------|
| **Alpha/Beta** | GitHub Releases | Full | $0 | Internal team |
| **Public RC** | Azure Blob + GitHub | Split (100MB chunks) | $2-5/mo | 100-1K |
| **Production** | S3 + MSIX/WinGet | Hosted | $5-15/mo | 1K+ |

---

## 📊 Splitting Large Archives (GitHub 2GB Limit)

If your app exceeds GitHub's file limits, split it:

```powershell
# Split into 500MB chunks
$archive = "RDAT.Copilot.App.zip"
$maxSize = 500MB

$sourceFile = Get-Item $archive
$chunks = [Math]::Ceiling($sourceFile.Length / $maxSize)

$sourceFile | Split-File -ChunkSizeBytes $maxSize -Prefix "RDAT.Copilot.App.part-"

# Users reassemble:
Get-Item "RDAT.Copilot.App.part-*" | 
  ForEach-Object { Get-Content $_.FullName -Encoding Byte } | 
  Set-Content "RDAT.Copilot.App.zip" -Encoding Byte
```

---

## ⚡ Quick Start - GitHub Actions Auto-Build

### Enable Auto-Build:
1. Push workflow file (done ✓)
2. Go to GitHub → **Actions** tab
3. Click **"Build RDAT Copilot Release"** workflow
4. Click **"Run workflow"** → **main branch**
5. Wait ~10-15 minutes for build

### Download Artifacts:
```
GitHub → Actions → Latest Run → Artifacts → Download
```

---

## 🔐 Security Considerations

- **Code Signing:** Sign .exe with certificate for trust
- **Antivirus Scanning:** VirusTotal scan before release
- **Update Checks:** Implement version checking in app
- **HTTPS Only:** Always use HTTPS for downloads

---

## 📋 Quick Reference: File Sizes

```
Expected Output:
├── RDAT.Copilot.App.exe        (~100 MB)
├── Dependencies (DLLs)          (~500 MB)
├── ONNX Models                  (~2.1 GB)
└── Total Zipped                 (~1.5-1.8 GB)
```

---

## 🎯 Immediate Actions

- [ ] Push workflow file to GitHub
- [ ] Trigger manual build in Actions
- [ ] Download and test .exe
- [ ] Choose primary distribution method
- [ ] Set up secondary backup storage
- [ ] Create public download page

---

## 📞 Next Steps

1. **Test GitHub Actions Build:**
   ```bash
   git push
   # or manually trigger in Actions tab
   ```

2. **Monitor Build Progress:**
   - GitHub Actions → Workflows → Build RDAT Copilot Release

3. **Download Artifact:**
   - Once complete, artifacts available for 30 days

4. **Choose Distribution:**
   - GitHub Releases (free, ≤2GB)
   - Azure Blob (best for large files, $2-5/mo)
   - AWS S3 (enterprise, $5-15/mo)


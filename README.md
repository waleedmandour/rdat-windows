# RDAT Copilot: Professional English-Arabic Translation Assistant

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue.svg)]()
[![Framework: .NET 8](https://img.shields.io/badge/Framework-.NET%208-purple.svg)]()
[![Build Status](https://img.shields.io/github/actions/workflow/status/waleedmandour/rdat-windows/build-release.yml?branch=main)]()

**RDAT Copilot** is a native Windows desktop application designed for professional translators and researchers. It provides **Google-like "Ghost Text" autocomplete** using local Large Language Models (LLMs), ensuring 100% data privacy for sensitive academic and legal translations.

This is the Windows desktop version of the [RDAT-PWA](https://github.com/waleedmandour/rdat-pwa) web application, rebuilt natively with WinUI 3 for superior performance and offline capabilities.

---

## Key Features

*   **Ghost Text Autocomplete:** Real-time, low-latency suggestions powered by **ONNX Runtime GenAI** and **DirectML** GPU acceleration. Press Tab to accept, Escape to reject.
*   **Gemini Cloud Fallback:** Optional Google Gemini 3.0 Flash integration for when local GPU is unavailable or insufficient. 100% offline by default.
*   **Semantic TM Search:** Local vector database (**LanceDB**) that retrieves contextually relevant translation pairs using ONNX sentence embeddings (MiniLM-L6-v2).
*   **AMTA Glossary Linter:** Automatic enforcement of professional terminology compliance using Aho-Corasick multi-pattern matching.
*   **Native .docx Integration:** Seamlessly process Word documents using DocumentFormat.OpenXml without requiring Microsoft Word installed.
*   **RTL/LTR Support:** Full bidirectional text support with Monaco Editor integration via WebView2.
*   **Privacy First:** Designed for **100% offline processing**. No data ever leaves your machine when using local models.

## AI Models & Databases

### Local AI (ONNX Runtime + DirectML)
| Model | Purpose | Size |
|-------|---------|------|
| Phi-3-mini-4k-instruct (DirectML INT4) | Ghost text generation | ~2.4 GB |
| all-MiniLM-L6-v2 (ONNX) | Semantic embeddings for TM search | ~90 MB |

### Cloud AI (Optional - Requires API Key)
| Service | Purpose |
|---------|---------|
| Google Gemini 3.0 Flash | Cloud fallback translation |

### Databases
| Database | Purpose |
|----------|---------|
| LanceDB | Vector Translation Memory with semantic search |
| Default Corpus | Built-in EN-AR translation pairs for immediate use |

## Architecture

Built on a **Clean Architecture** model with a strict separation between the AI logic and the UI:

- **Core:** Headless .NET 8 library containing the GhostTextCoordinator, interfaces, and models.
- **Infrastructure:** Native implementations for **ONNX GenAI**, **LanceDB vector search**, **Gemini API**, **AMTA linter**, and the **Monaco Editor Bridge**.
- **App (Desktop):** A modern **WinUI 3** (Windows App SDK 1.5.3) interface with Fluent Design and RTL support.

## Getting Started

### Prerequisites
- **Windows 10** (Build 19041) or **Windows 11**
- **NVIDIA/AMD GPU** (Recommended for DirectML acceleration)
- **.NET 8 SDK**

### Quick Start
1.  **Download the latest release** from [GitHub Releases](https://github.com/waleedmandour/rdat-windows/releases)
2.  **Extract the ZIP** to any folder
3.  **Run `RDAT.Copilot.App.exe`** — no installation required (portable)
4.  **Download AI models** using the built-in downloader or run:
    ```powershell
    ./scripts/download-models.ps1
    ```

### Build from Source
1.  **Clone the Repo:**
    ```bash
    git clone https://github.com/waleedmandour/rdat-windows.git
    ```
2.  **Download AI Models:**
    ```powershell
    ./scripts/download-models.ps1
    ```
3.  **Build & Run:**
    ```powershell
    dotnet restore RDAT.Copilot.sln
    dotnet build RDAT.Copilot.sln -c Release -p:Platform=x64
    ```
4.  **Build Portable .EXE:**
    ```powershell
    ./scripts/Build-RDAT.ps1 -Clean
    ```

## Research & Citation

If you use RDAT Copilot in your research or professional workflow, please cite it as follows:

```bibtex
@software{mandour2026rdatcopilot,
  author  = {Mandour, Waleed},
  title   = {RDAT Copilot: Native Windows Desktop App},
  year    = {2026},
  url     = {https://github.com/waleedmandour/rdat-windows},
  doi     = {10.17605/OSF.IO/GAQ4K}
}
```

## Privacy Guarantee

This application does not include any telemetry or cloud-uploading code. By default, all AI inference runs locally using ONNX Runtime with DirectML GPU acceleration. Cloud features (Gemini API) are optional and only activated when the user explicitly provides an API key.

Built with love for the Arabic Translation Community by Dr. Waleed Mandour.

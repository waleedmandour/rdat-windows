<div align="center">

# RDAT Copilot Desktop

**مساعد الترجمة الذكي — Repository-Driven Adaptive Translation**

[![Build](https://github.com/waleedmandour/rdat-windows/actions/workflows/build.yml/badge.svg)](https://github.com/waleedmandour/rdat-windows/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A native Windows 11 desktop CAT (Computer-Assisted Translation) tool built with WinUI 3, C# 12, and .NET 9. Ports the RDAT Copilot PWA architecture to a native Windows application with local AI inference via ONNX Runtime DirectML, disk-backed vector databases for massive Translation Memories, and Monaco Editor embedded via WebView2.

</div>

---

## Author & Affiliation

**Dr. Waleed Mandour**
Sultan Qaboos University (جامعة السلطان قابوس)
📧 [w.abumandour@squ.edu.om](mailto:w.abumandour@squ.edu.om)

---

## Technology Stack

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **UI Framework** | WinUI 3 (Windows App SDK 1.7) | Native Windows 11 shell, Mica backdrop |
| **Language** | C# 12 / .NET 9 | Type-safe, modern C# with file-scoped namespaces |
| **MVVM** | CommunityToolkit.Mvvm 8.4 | ObservableObject, RelayCommand, Ioc.Default |
| **Editor** | Monaco Editor via WebView2 | BiDi Arabic text, inline completions, markers |
| **Local AI** | OnnxRuntimeGenAI DirectML | Gemma 4 (INT4) on NPU/GPU |
| **Embeddings** | OnnxRuntime | paraphrase-multilingual-MiniLM-L12-v2 (384d) |
| **Vector DB** | LanceDB / SQLite-vec | Disk-backed RAG for 10M+ sentences |
| **Cloud AI** | HttpClient → Gemini API | BYOK cloud rewriting |
| **Security** | Windows Credential Locker | Secure API key storage |

## Architecture

```
┌──────────────────────────────────────────────────────┐
│                   WinUI 3 Shell                       │
│  ┌──────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │ Title Bar │  │  Split-Pane      │  │  Status Bar  │ │
│  └──────────┘  │  ┌─────┬───────┐  │  └──────────────┘ │
│                 │  │ SRC │ TGT   │  │                   │
│  ┌──────────┐   │  │WebView│WebView│ │                  │
│  │ Settings │   │  │ 2    │ 2    │ │                  │
│  │  Dialog  │   │  └─────┴───────┘  │                  │
│  └──────────┘   │     ↕ JSON Bridge  │                  │
│                 └────────┬──────────┘                   │
│  ┌──────────────────────┴──────────────────────────┐  │
│  │              Service Layer                        │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │  │
│  │  │ LLM Queue│ │ RAG/VEC  │ │ Grammar Checker  │ │  │
│  │  │ (DirectML)│ │(LanceDB) │ │ (ONNX)          │ │  │
│  │  └──────────┘ └──────────┘ └──────────────────┘ │  │
│  └──────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

## Phase Plan

| Phase | Description | Status |
|-------|------------|--------|
| **Phase 1** | WinUI 3 Scaffold + WebView2 Monaco Bridge | ✅ In Progress |
| **Phase 2** | Disk-Backed RAG & Massive Corpora (LanceDB) | 🔜 Pending |
| **Phase 3** | C# LLM Queue Engine (ONNX DirectML) | 🔜 Pending |
| **Phase 4** | AMTA Linter, Grammar Check & Gemini | 🔜 Pending |
| **Phase 5** | Native OS Integrations (.docx / Multi-window) | 🔜 Pending |

## Phase 1: WebView2 Monaco Bridge

The WinUI 3 shell provides native Windows 11 chrome (Mica material, custom title bar, NavigationView sidebar), while Monaco Editor renders inside WebView2 controls for unmatched text editing capabilities including bidirectional Arabic text, inline completions, and marker overlays.

**Interop Protocol:**

```
C# → JS:  CoreWebView2.PostWebMessageAsJson({ type: "command", command: "...", payload: {...} })
JS → C#:  window.chrome.webview.postMessage({ type: "event", event: "...", data: {...} })
```

### Project Structure

```
src/
├── RDAT.Copilot.Desktop/          # WinUI 3 app (Views, ViewModels, Services)
│   ├── Views/                     # XAML pages
│   ├── ViewModels/                # MVVM state management
│   ├── Services/                  # WebView2 bridge, navigation
│   ├── Helpers/                   # JSON serialization utilities
│   ├── Converters/                # XAML value converters
│   ├── Models/                    # Bridge message records
│   └── Assets/Monaco/             # HTML + JS bridge files
└── RDAT.Copilot.Core/             # Headless service layer (testable)
    ├── Constants/                 # Shared configuration
    ├── Models/                    # Domain models
    └── Interfaces/                # Service contracts (Phase 2-5)
```

## Development

### Prerequisites

- **Windows 11** (22H2 or later)
- **Visual Studio 2022 17.13+** with ".NET desktop development" workload
- **Windows App SDK 1.7** VSIX extension
- **.NET 9 SDK**

### Setup

```bash
git clone https://github.com/waleedmandour/rdat-windows.git
cd rdat-windows
dotnet restore
dotnet build
```

### Run

```bash
dotnet run --project src/RDAT.Copilot.Desktop
```

### Test

```bash
dotnet test
```

## Tri-Channel Ghost Text Architecture

The same proven architecture from the PWA, ported to C# with native threading:

| Channel | Trigger | Output | Preemption |
|---------|---------|--------|------------|
| **Ch3 (Prefetch)** | Source sentence change | Full dual-version | Low priority |
| **Ch5 (Burst)** | 0.8s typing pause | 3–5 words | High (preempts Ch3) |
| **Ch6 (Pause)** | 1.2s typing pause | 5–20 words | Highest (preempts Ch3) |

Preemption uses `CancellationTokenSource` — when a high-priority channel fires, it calls `.Cancel()` on the background Prefetch token to immediately free the DirectML queue.

## License

MIT — See [LICENSE](LICENSE) for details.

---

<div align="center">
<strong>Dr. Waleed Mandour</strong> · Sultan Qaboos University · جامعة السلطان قابوس
📧 [w.abumandour@squ.edu.om](mailto:w.abumandour@squ.edu.om)

Built with WinUI 3 · .NET 9 · WebView2 · ONNX Runtime · LanceDB · Monaco Editor
</div>

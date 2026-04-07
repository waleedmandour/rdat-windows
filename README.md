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
| **Embeddings** | ONNX Runtime 1.21 | paraphrase-multilingual-MiniLM-L12-v2 (384d) |
| **Vector DB** | LanceDB 0.14 | Disk-backed RAG for 10M+ sentences |
| **LLM Engine** | ONNX Runtime GenAI 0.6 | Gemma 2B IT INT4 (DirectML GPU/NPU) |
| **Queue** | System.Threading.Channels | Priority queue with preemption |
| **CSV Parsing** | CsvHelper 33.0 | TM file import (CSV, TMX, TSV) |
| **Cloud AI** | HttpClient → Gemini API | BYOK cloud rewriting |
| **Security** | Windows Credential Locker | Secure API key storage |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      WinUI 3 Shell                           │
│  ┌──────────┐  ┌───────────────────┐  ┌──────────────────┐  │
│  │ Title Bar │  │  Split-Pane        │  │  TM Panel (P2)  │  │
│  └──────────┘  │  ┌───────┬────────┐│  │  ┌────────────┐ │  │
│                 │  │  SRC  │  TGT   ││  │  │ Search     │ │  │
│  ┌──────────┐  │  │WebView│WebView ││  │  │ Results    │ │  │
│  │ Settings │  │  │  2    │  2     ││  │  │ Import     │ │  │
│  │  Page    │  │  └───────┴────────┘│  │  │ Stats      │ │  │
│  └──────────┘  │    ↕ JSON Interop   │  │  └────────────┘ │  │
│                 └──────────┬──────────┘  └────────┬─────────┘  │
│  ┌────────────────────────┴───────────────────────┴────────┐  │
│  │                  Service Layer (MVVM)                    │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────┐ │  │
│  │  │ LLM Queue│ │ RAG Pipe │ │ Grammar  │ │ TM Import │ │  │
│  │  │(DirectML)│ │(LanceDB) │ │ Checker  │ │ (TMX/CSV) │ │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └───────────┘ │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                 Core Library (Headless)                  │  │
│  │  ┌────────────┐  ┌────────────┐  ┌──────────────────┐  │  │
│  │  │  Embedding  │  │  VectorDB  │  │  Domain Models   │  │  │
│  │  │  (ONNX)    │  │  (LanceDB) │  │  TM/RAG/Lang     │  │  │
│  │  └────────────┘  └────────────┘  └──────────────────┘  │  │
│  └────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Phase Plan

| Phase | Description | Status |
|-------|------------|--------|
| **Phase 1** | WinUI 3 Scaffold + WebView2 Monaco Bridge | ✅ Complete |
| **Phase 2** | Disk-Backed RAG & Massive Corpora (LanceDB) | ✅ Complete |
| **Phase 3** | C# LLM Queue Engine (ONNX DirectML) | ✅ Complete |
| **Phase 4** | AMTA Linter, Grammar Check & Gemini | 🔜 Pending |
| **Phase 5** | Native OS Integrations (.docx / Multi-window) | 🔜 Pending |

## Phase 1: WebView2 Monaco Bridge ✅

The WinUI 3 shell provides native Windows 11 chrome (Mica material, custom title bar), while Monaco Editor renders inside WebView2 controls for unmatched text editing capabilities including bidirectional Arabic text, inline completions, and marker overlays.

**Interop Protocol:**

```
C# → JS:  CoreWebView2.PostWebMessageAsJson({ type: "command", command: "...", payload: {...} })
JS → C#:  window.chrome.webview.postMessage({ type: "event", event: "...", data: {...} })
```

## Phase 2: RAG Pipeline & Translation Memory ✅

### Overview

Phase 2 introduces the Retrieval-Augmented Generation (RAG) pipeline for Translation Memory lookup. The system uses local ONNX Runtime embedding inference and LanceDB vector search to find verified TM matches in real-time (<50ms target) and present them as GTR (Guaranteed Translation Result) ghost text in the target editor.

### RAG Pipeline Flow

```
Source Sentence
    ↓
ONNX Embedding (paraphrase-multilingual-MiniLM-L12-v2)
    ↓ 384-dim normalized vector
LanceDB Vector Search (cosine similarity)
    ↓ top-K results (default: K=5)
Score Filtering (minimum threshold: 0.5)
    ↓
TM Search Results → GTR Ghost Text Channel (score ≥ 0.7)
    ↓
Monaco Inline Completion [GTR 92%] TM Match
```

### Four-Priority Ghost Text Architecture

| Priority | Channel | Trigger | Source | Preemption |
|----------|---------|---------|--------|------------|
| 1 | **GTR** (Phase 2) | Source line change | LanceDB TM match (≥70%) | Always visible |
| 2 | **Pause** (Ch6) | 1.2s typing pause | Local LLM continuation | Preempts GTR if typing |
| 3 | **Burst** (Ch5) | 0.8s typing pause | Local LLM autocomplete | Preempts GTR if typing |
| 4 | **Prefetch** (Ch3) | Sentence change | Background LLM | Cancelled by higher priority |

### Supported TM Import Formats

| Format | Extension | Description |
|--------|-----------|-------------|
| CSV | `.csv` | Columns: source, target (optional: domain, quality) |
| TMX 1.4 | `.tmx` | Translation Memory eXchange XML standard |
| TSV | `.tsv` / `.txt` | Tab-separated: `source\ttarget` per line |

### Key Components

- **OnnxEmbeddingService** — Local ONNX Runtime inference for multilingual embeddings (384 dimensions, L2-normalized for cosine similarity)
- **LanceVectorDbService** — Disk-backed LanceDB table for persistent vector storage (supports 10M+ entries without RAM pressure)
- **RagPipelineService** — Orchestrates embed → search → filter pipeline with batch processing for TM import
- **TmImportService** — Parses TMX 1.4, CSV, and TSV files into TmEntry records
- **TmPanelViewModel** — MVVM control for the TM sidebar panel (search, import, browse)
- **WeakReferenceMessenger** — Decoupled JS→ViewModel event dispatch via CommunityToolkit.Mvvm

### Project Structure

```
src/
├── RDAT.Copilot.Desktop/          # WinUI 3 app (Views, ViewModels, Services)
│   ├── Views/
│   │   ├── WorkspacePage.xaml     # Split-pane editors + TM panel
│   │   └── SettingsPage.xaml      # Config + RAG pipeline initialization
│   ├── ViewModels/
│   │   ├── WorkspaceViewModel.cs  # RAG state tracking, GTR channel
│   │   ├── TmPanelViewModel.cs    # TM search/import/browse
│   │   └── SettingsViewModel.cs   # Model path, DB path config
│   ├── Services/
│   │   └── WebViewBridgeService.cs # RAG ghost text commands + messenger
│   └── Assets/Monaco/
│       └── monaco-bridge.js       # GTR priority channel in inline provider
└── RDAT.Copilot.Core/             # Headless service layer (testable)
    ├── Services/
    │   ├── OnnxEmbeddingService.cs # ONNX multilingual embedding
    │   ├── LanceVectorDbService.cs # LanceDB vector operations
    │   ├── RagPipelineService.cs   # End-to-end RAG orchestration
    │   └── TmImportService.cs      # TMX/CSV/TSV parsing
    ├── Models/
    │   ├── TranslationMemory.cs    # TmEntry, TmSearchResult, LanceTmRow
    │   ├── GhostTextSuggestion.cs  # RagState, SuggestionMode enums
    │   └── GrammarIssue.cs         # Grammar error models
    ├── Interfaces/
    │   ├── IRagPipelineService.cs  # RAG pipeline contract
    │   ├── IEmbeddingService.cs    # Embedding model contract
    │   ├── IVectorDatabaseService.cs # Vector DB contract
    │   ├── ITmImportService.cs     # TM file import contract
    │   ├── ILocalInferenceService.cs # LLM inference (Phase 3)
    │   └── IGrammarCheckerService.cs # Grammar check (Phase 4)
    └── Constants/
        └── AppConstants.cs         # Embedding dims, search limits
```

## Phase 3: LLM Queue Engine ✅

### Overview

Phase 3 implements the local LLM inference engine with a priority queue for managed GPU/NPU resource allocation. The system runs Gemma 2B IT INT4 quantized model via ONNX Runtime GenAI with DirectML backend, processing exactly one generation request at a time through a `System.Threading.Channels`-based priority queue.

### Queue Architecture

```
Editor Event (cursor/text change)
    ↓
GhostTextCoordinator (debounce timers)
    ↓
Channel Handler → LlmRequest (with priority + CancellationTokenSource)
    ↓
LlmQueueService (Channel<LlmRequest> → single consumer loop)
    ↓                      ↓
    |               Preemption check:
    |               if new.Priority > current.Priority → current.Cancel()
    ↓
OnnxLlmInferenceService (ONNX Runtime GenAI + DirectML)
    ↓
LlmGenerationResult → GhostTextCoordinator → WorkspacePage → Monaco
```

### Preemption Strategy

| Scenario | Action | Result |
|----------|--------|--------|
| Burst while Prefetch running | Cancel Prefetch CTS | DirectML queue freed for Burst |
| Pause while Burst running | Cancel Burst CTS | DirectML queue freed for Pause |
| Prefetch while Pause running | Queue behind Pause | No preemption (lower priority) |

### Key Components

- **OnnxLlmInferenceService** — ONNX Runtime GenAI wrapper with DirectML, Gemma chat format, reflection-based API, warm-up
- **LlmQueueService** — `System.Threading.Channels` single-consumer loop, priority preemption, per-channel statistics
- **GhostTextCoordinator** — Debounce timers (800ms Burst, 1200ms Pause), prompt builders with RAG augmentation, event routing
- **LlmRequest / LlmGenerationResult** — Typed request/response models with priority, cancellation, timing
- **ChannelStats** — Per-channel statistics (total, success, preemptions, errors, average latency)

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

## License

MIT — See [LICENSE](LICENSE) for details.

---

<div align="center">
<strong>Dr. Waleed Mandour</strong> · Sultan Qaboos University · جامعة السلطان قابوس
📧 [w.abumandour@squ.edu.om](mailto:w.abumandour@squ.edu.om)

Built with WinUI 3 · .NET 9 · WebView2 · ONNX Runtime · LanceDB · Monaco Editor
</div>

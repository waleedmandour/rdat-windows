# RDAT Copilot: Professional English-Arabic Translation Assistant

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011-blue.svg)]()
[![Framework: .NET 8](https://img.shields.io/badge/Framework-.NET%208-purple.svg)]()

**RDAT Copilot** is a native Windows 11 application designed for professional translators and researchers. It provides **Google-like "Ghost Text" autocomplete** using local Large Language Models (LLMs), ensuring 100% data privacy for sensitive academic and legal translations.

---

## ✨ Key Features

*   🤖 **Ghost Text Autocomplete:** Real-time, low-latency suggestions powered by **ONNX Runtime GenAI** and **DirectML** GPU acceleration.
*   🔍 **Semantic TM Search:** Local vector database (**LanceDB**) that retrieves contextually relevant translation pairs in <10ms.
*   ✅ **AMTA Glossary Linter:** Automatic enforcement of professional feedback schemas and terminology compliance.
*   📄 **Native .docx Integration:** Seamlessly process Word documents without requiring Microsoft Word installed.
*   🔒 **Privacy First:** Designed for **100% offline processing**. No data ever leaves your machine when using local models.

## 🏗️ Technical Architecture

Built on a **Clean Architecture** model with a strict separation between the AI logic and the UI:

- **Core:** Headless .NET 8 library containing the "Brain" (Orchestration, Contracts, and Priority Queues).
- **Infrastructure:** Native implementations for **DirectML**, **Vector Search**, and the **Monaco Editor Bridge**.
- **Desktop:** A modern **WinUI 3** (Windows App SDK) interface with Fluent Design and RTL support.

## 🚀 Getting Started

### Prerequisites
- **Windows 11** (Build 22621 or higher)
- **NVIDIA/AMD GPU** (Recommended for DirectML acceleration)
- **Visual Studio 2022** with .NET 8 SDK

### Installation
1.  **Clone the Repo:**
    ```bash
    git clone https://github.com/waleedmandour/rdat-windows.git
    ```
2.  **Download AI Models:**
    Run the setup script to pull the optimized ONNX models (Phi-3 & MiniLM):
    ```powershell
    ./scripts/download-models.ps1
    ```
3.  **Build & Run:**
    Open `RDAT.Copilot.sln` in Visual Studio 2022 and hit **F5**.

## 📚 Research & Citation

If you use RDAT Copilot in your research or professional workflow, please cite it as follows:

```bibtex
@software{mandour2026rdatcopilot,
  author  = {Mandour, Waleed},
  title   = {RDAT Copilot: Native Windows 11 Desktop App},
  year    = {2026},
  url     = {https://github.com/waleedmandour/rdat-windows},
  doi     = {10.17605/OSF.IO/GAQ4K}
}
```

## 🔐 Privacy Guarantee
This application does not include any telemetry or cloud-uploading code. The compiler-enforced BannedApiAnalyzers prevent HttpClient usage within the core logic, ensuring your translation data stays on your local hardware.

Built with ❤️ for the Arabic Translation Community by Dr. Waleed Mandour.


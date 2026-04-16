// ========================================================================
// RDAT Copilot - Core Interfaces
// Location: src/RDAT.Copilot.Core/Interfaces/
// ========================================================================

using System.Reactive.Subjects;

namespace RDAT.Copilot.Core.Interfaces;

// ========================================================================
// LLM Inference Service
// ========================================================================

/// <summary>
/// Abstraction for local ONNX/DirectML inference and optional Gemini cloud fallback.
/// </summary>
public interface ILlmInferenceService : IAsyncDisposable
{
    /// <summary>Loads the ONNX model from the specified directory.</summary>
    Task LoadModelAsync(string modelPath, CancellationToken ct = default);

    /// <summary>Generates a streaming translation for the given source text.</summary>
    IAsyncEnumerable<string> GenerateStreamingAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default);

    /// <summary>Generates a greedy-decode ghost text prediction (low-latency).</summary>
    Task<GhostTextResult> GetPredictionAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default);

    /// <summary>True when the model is loaded and ready for inference.</summary>
    bool IsModelLoaded { get; }

    /// <summary>Current inference mode (DirectML GPU, CPU, or Cloud).</summary>
    string InferenceMode { get; }
}

// ========================================================================
// Semantic Translation Memory Service
// ========================================================================

/// <summary>
/// Vector-search based Translation Memory using LanceDB + ONNX embeddings.
/// </summary>
public interface ISemanticTmService : IAsyncDisposable
{
    /// <summary>Opens the TM database and loads the embedding model.</summary>
    Task OpenAsync(string dbPath, CancellationToken ct = default);

    /// <summary>Searches for semantically similar translation pairs.</summary>
    Task<IReadOnlyList<TmSearchResult>> SearchSimilarContextAsync(
        string sourceText, int maxResults = 5, CancellationToken ct = default);

    /// <summary>Finds an exact match in the TM.</summary>
    Task<TmSearchResult?> FindExactMatchAsync(
        string sourceText, CancellationToken ct = default);

    /// <summary>Bulk inserts translation pairs into the TM.</summary>
    Task BulkUpsertAsync(
        IReadOnlyList<TranslationPair> pairs, CancellationToken ct = default);

    /// <summary>Total number of entries in the TM.</summary>
    int EntryCount { get; }
}

// ========================================================================
// AMTA Glossary Linter Service
// ========================================================================

/// <summary>
/// Terminology linter using Aho-Corasick multi-pattern matching.
/// Validates AI predictions against the user's approved glossary.
/// </summary>
public interface IAmtaLinterService : IDisposable
{
    /// <summary>Loads the glossary from a JSON file and builds the automaton.</summary>
    Task LoadGlossaryAsync(string glossaryPath, CancellationToken ct = default);

    /// <summary>Checks a suggestion for terminology violations.</summary>
    LintResult Lint(string suggestion);

    /// <summary>Attempts to auto-correct violations. Returns true if corrected.</summary>
    bool TryAutoCorrect(ref string suggestion, out string corrected);

    /// <summary>Combined lint + auto-correct in one pass.</summary>
    LintResult LintAndCorrect(ref string suggestion);

    /// <summary>Hot-reloads the glossary from disk.</summary>
    void ReloadGlossary();

    /// <summary>Current glossary entry count.</summary>
    int GlossaryCount { get; }

    /// <summary>Fires when a violation is detected.</summary>
    event Action<GlossaryViolation>? OnViolationDetected;
}

// ========================================================================
// Editor Bridge (WebView2 <-> Monaco)
// ========================================================================

/// <summary>
/// Bidirectional bridge between C# application logic and Monaco Editor
/// running inside a WinUI 3 WebView2 control.
/// </summary>
public interface IEditorBridge : IDisposable
{
    /// <summary>Attaches to a WebView2 instance and starts listening.</summary>
    void Attach(Microsoft.UI.Xaml.Controls.WebView2 webView);

    /// <summary>Observable stream of keystroke events from the editor.</summary>
    IObservable<EditorKeystroke> KeystrokeStream { get; }

    /// <summary>Observable stream for pushing ghost text results back to JS.</summary>
    IObservable<GhostTextResult> GhostTextStream { get; }

    /// <summary>Posts a ghost text result to the Monaco editor.</summary>
    void PostGhostTextResult(GhostTextResult result);

    /// <summary>Sets the editor directionality (RTL/LTR).</summary>
    void SetDirection(bool isRtl);

    /// <summary>Clears the current ghost text display.</summary>
    void ClearGhostText();
}

namespace RDAT.Copilot.Core.Constants;

/// <summary>
/// Application-wide constants shared across all layers.
/// </summary>
public static class AppConstants
{
    // ─── App Info ────────────────────────────────────────────────────
    public const string AppName = "RDAT Copilot";
    public const string AppShortName = "RDAT";
    public const string AppVersion = "2.0.0";

    // ─── Editor ─────────────────────────────────────────────────────
    public const int EditorDebounceMs = 300;
    public const int GhostTextDebounceMs = 150;

    // ─── Channel Timings ────────────────────────────────────────────
    public const int BurstDebounceMs = 800;
    public const int BurstMaxTokens = 50;
    public const int PauseDebounceMs = 1200;
    public const int PauseMaxTokens = 100;
    public const int PauseMinDraftLength = 3;
    public const int PrefetchMaxTokens = 200;

    // ─── Grammar Checker ────────────────────────────────────────────
    public const int GrammarCheckDebounceMs = 2500;
    public const int GrammarCheckMaxTokens = 300;
    public const int GrammarCheckBatchSize = 5;

    // ─── AMTA Linter ────────────────────────────────────────────────
    public const int AmtaLintDebounceMs = 2000;
    public const int AmtaMinTermLength = 3;

    // ─── RAG / Vector DB ────────────────────────────────────────────
    public const int EmbeddingDimensions = 384;
    public const int RagSearchLimit = 5;
    public const int RagSearchTargetMs = 50;

    // ─── Local AI ───────────────────────────────────────────────────
    public const string DefaultModelId = "gemma-2b-it-q4f32_1-ONNX";
    public const float DefaultTemperature = 0.3f;

    // ─── Cloud AI ───────────────────────────────────────────────────
    public const string GeminiModelId = "gemini-2.0-flash";
    public const string GeminiApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";
}

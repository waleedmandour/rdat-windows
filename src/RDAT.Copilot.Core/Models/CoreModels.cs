// ========================================================================
// RDAT Copilot - Core Models
// Location: src/RDAT.Copilot.Core/Models/
// ========================================================================

namespace RDAT.Copilot.Core.Models;

/// <summary>
/// A source-target translation pair used in TM and document segments.
/// </summary>
public sealed record TranslationPair
{
    public string Source { get; init; } = "";
    public string Target { get; init; } = "";
    public string Domain { get; init; } = "General";
    public bool IsConfirmed { get; init; }
}

/// <summary>
/// A translatable document segment extracted from a .docx file.
/// </summary>
public sealed record DocumentSegment
{
    public int SegmentId { get; init; }
    public string Source { get; init; } = "";
    public string Target { get; init; } = "";
    public string Domain { get; init; } = "General";
    public bool IsConfirmed { get; init; }
}

/// <summary>
/// Ghost text prediction result from the AI model.
/// </summary>
public sealed record GhostTextResult
{
    public string Text { get; init; } = "";
    public double Confidence { get; init; }
    public long LatencyMs { get; init; }
    public string Source { get; init; } = "local"; // "local" | "cloud"
    public bool IsSuppressed { get; init; }
}

/// <summary>
/// A Translation Memory search result with similarity score.
/// </summary>
public sealed record TmSearchResult
{
    public string SourceText { get; init; } = "";
    public string TargetText { get; init; } = "";
    public double SimilarityScore { get; init; }
    public string Domain { get; init; } = "General";
    public DateTimeOffset LastUsed { get; init; }
}

/// <summary>
/// Keystroke event from the Monaco Editor via WebView2 bridge.
/// </summary>
public sealed record EditorKeystroke
{
    public string SourceText { get; init; } = "";
    public string TargetText { get; init; } = "";
    public string Language { get; init; } = "en";
    public bool IsRtl { get; init; }
}

/// <summary>
/// Glossary entry mapping a source term to an approved target term.
/// </summary>
public sealed class GlossaryEntry
{
    public string SourceTerm { get; set; } = "";
    public string TargetTerm { get; set; } = "";
    public string Direction { get; set; } = "en→ar";
    public string Domain { get; set; } = "General";
    public string? ForbiddenTerm { get; set; }
    public List<string>? KnownMistranslations { get; set; }
}

/// <summary>
/// A terminology violation detected by the AMTA linter.
/// </summary>
public sealed class GlossaryViolation
{
    public required GlossaryEntry Entry { get; init; }
    public required string ForbiddenTerm { get; init; }
    public int StartIndex { get; init; }
    public int Length { get; init; }
    public bool WasAutoCorrected { get; set; }
}

/// <summary>
/// Result of linting an AI suggestion against the glossary.
/// </summary>
public sealed class LintResult
{
    public bool IsClean { get; init; }
    public IReadOnlyList<GlossaryViolation> Violations { get; init; } = [];
    public string? CorrectedText { get; set; }
    public bool ShouldSuppress { get; set; }
}

/// <summary>
/// Hardware capability information for inference backend selection.
/// </summary>
public sealed record HardwareCapabilities
{
    public string GpuName { get; init; } = "Unknown";
    public long DedicatedVramBytes { get; init; }
    public long SharedVramBytes { get; init; }
    public string DriverVersion { get; init; } = "";
    public bool IsDirectMlSupported { get; init; }
    public string? DirectMlVersion { get; init; }
    public int HardwareScore { get; init; }
}

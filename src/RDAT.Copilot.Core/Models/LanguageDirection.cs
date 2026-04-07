namespace RDAT.Copilot.Core.Models;

/// <summary>
/// Supported translation language directions.
/// </summary>
public enum LanguageDirection
{
    /// <summary>English source → Arabic target.</summary>
    EnToAr,

    /// <summary>Arabic source → English target.</summary>
    ArToEn
}

/// <summary>
/// Language pair descriptor with bilingual labels.
/// </summary>
public record LanguagePair(
    string Source,
    string Target,
    string SourceLabel,
    string TargetLabel,
    string SourceLabelAr,
    string TargetLabelAr
);

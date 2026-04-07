using Microsoft.UI;
using Microsoft.UI.Xaml.Data;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Converts a double score (0.0-1.0) to a percentage string (e.g., "85%").
/// Used for displaying TM match confidence scores in the TM panel.
/// </summary>
public class ScoreToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double score && score >= 0.0 && score <= 1.0)
            return $"{score:P0}";
        if (value is float f && f >= 0f && f <= 1f)
            return $"{f:P0}";
        return value?.ToString() ?? "0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a double millisecond value to a formatted duration string.
/// Formats as "123 ms" for values under 1000ms, "1.2s" for larger values.
/// </summary>
public class MsFormatter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double ms)
        {
            if (ms < 1000)
                return $"{ms:F0} ms";
            return $"{ms / 1000:F1}s";
        }
        if (value is float fMs)
        {
            if (fMs < 1000)
                return $"{fMs:F0} ms";
            return $"{fMs / 1000:F1}s";
        }
        return value?.ToString() ?? "0 ms";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a GrammarErrorType enum value to a Segoe MDL icon glyph.
/// Maps: Spelling→⚠, Grammar→✗, Punctuation→❗, Style→💡
/// </summary>
public class GrammarTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "spelling" => "\uE8D2",  // Warning
            "grammar" => "\uE783",  // ErrorBadge
            "punctuation" => "\uE8F1", // Info
            "style" => "\uE946",      // Lightbulb
            _ => "\uE8D2"             // Default warning
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a GrammarErrorType enum value to a SolidColorBrush color.
/// Maps: Spelling→red, Grammar→red, Punctuation→amber, Style→purple
/// </summary>
public class GrammarTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "spelling" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xEF, 0x44, 0x44)),    // Red
            "grammar" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xEF, 0x44, 0x44)),    // Red
            "punctuation" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF5, 0x9E, 0x0B)), // Amber
            "style" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xA7, 0x8B, 0xFA)),   // Purple
            _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xEF, 0x44, 0x44))     // Default red
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

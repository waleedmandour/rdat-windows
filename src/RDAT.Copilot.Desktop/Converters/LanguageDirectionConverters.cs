using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Converts a LanguageDirection enum value to bool for RadioButton binding.
/// Returns true when the value matches the ConverterParameter string.
/// Usage: ConverterParameter="EnToAr" or ConverterParameter="ArToEn"
/// </summary>
public class LanguageDirectionToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LanguageDirection dir && parameter is string target)
        {
            return dir.ToString().Equals(target, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is true && parameter is string target && Enum.TryParse<LanguageDirection>(target, true, out var result))
        {
            return result;
        }
        return DependencyProperty.UnsetValue;
    }
}

/// <summary>
/// Converts a LanguageDirection enum value to FlowDirection for RTL/LTR layout.
/// EnToAr → LeftToRight (source is English, target is Arabic — editor is LTR with RTL target pane)
/// ArToEn → RightToLeft (source is Arabic, target is English — swap layout)
/// </summary>
public class LanguageDirectionToFlowDirectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LanguageDirection direction)
        {
            // When translating En→Ar, the overall editor layout stays LTR
            // When translating Ar→En, the source pane is Arabic so the target content is English
            // FlowDirection on the target pane:
            //   - EnToAr target = Arabic → RightToLeft
            //   - ArToEn target = English → LeftToRight
            return direction == LanguageDirection.EnToAr
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts AmtaLintSeverity to a color hex string for the issue list.
/// Error → #fca5a5 (red), Warning → #fcd34d (amber), Info → #93c5fd (blue)
/// </summary>
public class AmtaSeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is AmtaLintSeverity severity)
        {
            return severity switch
            {
                AmtaLintSeverity.Error => "#fca5a5",
                AmtaLintSeverity.Warning => "#fcd34d",
                AmtaLintSeverity.Info => "#93c5fd",
                _ => "#c4b5fd"
            };
        }
        return "#c4b5fd";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts AmtaLintSeverity to a FontIcon glyph.
/// Error → E783 (error badge), Warning → E7BA (warning), Info → E946 (info)
/// </summary>
public class AmtaSeverityToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is AmtaLintSeverity severity)
        {
            return severity switch
            {
                AmtaLintSeverity.Error => "\uE783",
                AmtaLintSeverity.Warning => "\uE7BA",
                AmtaLintSeverity.Info => "\uE946",
                _ => "\uE946"
            };
        }
        return "\uE946";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

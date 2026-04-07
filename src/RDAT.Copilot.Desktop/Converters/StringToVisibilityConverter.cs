using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Converts a string value to Visibility.
/// Returns Visible when the string is not null/empty, Collapsed otherwise.
/// Used for conditional UI elements like quality feedback display.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

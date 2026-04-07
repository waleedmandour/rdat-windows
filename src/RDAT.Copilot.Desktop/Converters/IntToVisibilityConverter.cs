using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Converts an integer value to Visibility.
/// Returns Visible when the value is greater than 0, Collapsed otherwise.
/// Used to show/hide badge counts in the editor tab bar.
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int intValue)
            return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is double doubleValue)
            return doubleValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

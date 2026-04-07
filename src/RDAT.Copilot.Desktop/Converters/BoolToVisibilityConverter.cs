using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Converts bool to Visibility (true → Visible, false → Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility.Visible;
    }
}

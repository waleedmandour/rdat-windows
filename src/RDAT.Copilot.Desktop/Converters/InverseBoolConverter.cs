using Microsoft.UI.Xaml.Data;

namespace RDAT.Copilot.Desktop.Converters;

/// <summary>
/// Inverts a boolean value (true → false, false → true).
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }
}

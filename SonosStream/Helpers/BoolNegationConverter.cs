using Microsoft.UI.Xaml.Data;

namespace SonosStream.Helpers;

/// <summary>
/// XAML value converter that negates a boolean (used to disable a button while busy).
/// </summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is bool b && !b;
}

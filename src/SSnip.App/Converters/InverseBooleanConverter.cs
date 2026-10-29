using System.Globalization;
using System.Windows.Data;

namespace SSnip.App.Converters;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Collapsed when true, visible when false.</summary>
[ValueConversion(typeof(bool), typeof(System.Windows.Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

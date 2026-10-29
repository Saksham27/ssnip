using System.Globalization;
using System.Windows.Data;

namespace SSnip.App.Converters;

/// <summary>True when the value is not null (and not an empty string).</summary>
[ValueConversion(typeof(object), typeof(bool))]
public sealed class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && value is not "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

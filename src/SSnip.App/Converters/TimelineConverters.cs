using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SSnip.App.Converters;

/// <summary>
/// Places a time on the timeline: values are the time (a <see cref="TimeSpan"/> or milliseconds), the
/// media length in milliseconds and the timeline width; the result is the x offset in pixels.
/// </summary>
public sealed class TimeToOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < 3 || Milliseconds(values[0]) is not { } time || values[1] is not long length || length <= 0 || values[2] is not double width)
        {
            return 0d;
        }

        return Math.Clamp(time / length, 0, 1) * width;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static double? Milliseconds(object value) => value switch
    {
        TimeSpan span => span.TotalMilliseconds,
        long ms => ms,
        _ => null,
    };
}

/// <summary>Width of a range on the timeline: values are start, end, media length (ms) and timeline width.</summary>
public sealed class RangeToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < 4
            || TimeToOffsetConverter.Milliseconds(values[0]) is not { } start
            || TimeToOffsetConverter.Milliseconds(values[1]) is not { } end
            || values[2] is not long length || length <= 0 || values[3] is not double width)
        {
            return 0d;
        }

        return Math.Max(2, Math.Clamp((end - start) / length, 0, 1) * width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows an element only when a nullable value has one.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

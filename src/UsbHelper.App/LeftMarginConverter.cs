using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UsbHelper.App;

/// <summary>
/// For a line of a philosophy's details panel (#397): its indent as a left margin, and a heading's weight and a quote's
/// style.
/// </summary>
public sealed class LeftMarginConverter : IValueConverter
{
    public static LeftMarginConverter Instance { get; } = new();

    public static FuncValueConverter<bool, FontWeight> Bold { get; } = new(heading => heading ? FontWeight.SemiBold : FontWeight.Normal);

    public static FuncValueConverter<bool, FontStyle> Italic { get; } = new(quote => quote ? FontStyle.Italic : FontStyle.Normal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is double left ? left : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

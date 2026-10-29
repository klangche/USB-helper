using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UsbHelper.App;

/// <summary>Turns a colour such as "#2E7D32" from a view model into a brush.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex ? new SolidColorBrush(Color.Parse(hex)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

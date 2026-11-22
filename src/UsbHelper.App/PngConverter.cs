using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace UsbHelper.App;

/// <summary>
/// Turns a PNG file path or PNG bytes from a view model into a bitmap (#340). A file is read into memory first, so it is
/// not held open and can be replaced; one that cannot be read shows nothing.
/// </summary>
public sealed class PngConverter : IValueConverter
{
    public static readonly PngConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            return value switch
            {
                byte[] png => new Bitmap(new MemoryStream(png)),
                string path => new Bitmap(new MemoryStream(File.ReadAllBytes(path))),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

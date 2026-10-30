using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.Core.Database;

/// <summary>
/// The id of a computer profile, computed from manufacturer and model as the computer reports them. Never written in
/// a profile file, so nobody can set or change it: the same name always gives the same id.
/// </summary>
public static class ComputerProfileId
{
    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int Length = 6;

    /// <summary>6 lowercase letters and digits. Case, extra spaces and accent encoding in the names do not matter.</summary>
    public static string From(string manufacturer, string model)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Normalize(manufacturer)}\n{Normalize(model)}"));
        var value = BinaryPrimitives.ReadUInt64BigEndian(hash);
        var id = new char[Length];
        for (var i = Length - 1; i >= 0; i--)
        {
            id[i] = Digits[(int)(value % (ulong)Digits.Length)];
            value /= (ulong)Digits.Length;
        }
        return new string(id);
    }

    // NFC first, so a name typed with combined or separate accents gives the same id.
    private static string Normalize(string text) =>
        string.Join(' ', text.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}

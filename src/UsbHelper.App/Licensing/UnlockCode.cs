using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.App.Licensing;

/// <summary>
/// The short unlock code (#234): <c>UH-XXXXX-XXXXX-XXXXX-XXXXX</c>, 12 bytes in Crockford Base32. Byte 0 holds the keyId
/// (high nibble) and the features (low nibble); bytes 1-11 are the first 11 bytes of
/// <c>HMAC-SHA256(secret[keyId], "USBH2|&lt;email&gt;|&lt;keyId&gt;|&lt;features&gt;")</c>, the email trimmed and lower case.
/// </summary>
public static class UnlockCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Bytes = 12;
    private const int Characters = 20; // 100 bits: the 96 of the code, then 4 zero bits

    // Every Feature flag; a code with any other bit is refused.
    private static readonly int KnownFeatures = Enum.GetValues<Feature>().Aggregate(0, (all, feature) => all | (int)feature);

    /// <summary>The code for <paramref name="email"/>: the reference for the keygen and the tests.</summary>
    public static string Make(byte[] secret, int keyId, int features, string email)
    {
        var bytes = new byte[Bytes];
        bytes[0] = (byte)(keyId << 4 | features);
        Tag(secret, keyId, features, email).CopyTo(bytes, 1);
        var text = Encode(bytes);
        return "UH-" + string.Join('-', text.Chunk(5).Select(group => new string(group)));
    }

    /// <summary>The features the code unlocks for <paramref name="email"/>, or null when it does not match.</summary>
    /// <param name="keys">The secrets of this build by keyId; a code made with any other key is refused.</param>
    public static int? Verify(IReadOnlyDictionary<int, byte[]> keys, string email, string code)
    {
        if (string.IsNullOrWhiteSpace(email) || Decode(code) is not { } bytes)
        {
            return null;
        }
        var keyId = bytes[0] >> 4;
        var features = bytes[0] & 0xF;
        if (features == 0 || (features & ~KnownFeatures) != 0 || !keys.TryGetValue(keyId, out var secret))
        {
            return null;
        }
        return CryptographicOperations.FixedTimeEquals(Tag(secret, keyId, features, email), bytes.AsSpan(1)) ? features : null;
    }

    private static byte[] Tag(byte[] secret, int keyId, int features, string email)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"USBH2|{email.Trim().ToLowerInvariant()}|{keyId}|{features}");
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(message))[..(Bytes - 1)];
    }

    private static string Encode(byte[] bytes)
    {
        var text = new char[Characters];
        for (var i = 0; i < Characters; i++)
        {
            var value = 0;
            for (var bit = i * 5; bit < i * 5 + 5; bit++)
            {
                value = value << 1 | (bit < Bytes * 8 ? bytes[bit / 8] >> (7 - bit % 8) & 1 : 0);
            }
            text[i] = Alphabet[value];
        }
        return new string(text);
    }

    // Ignores case, spaces, dashes and the UH prefix; reads O as 0 and I or L as 1. Null when it is not a code.
    private static byte[]? Decode(string code)
    {
        var text = code.ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
            .Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (text.StartsWith("UH", StringComparison.Ordinal))
        {
            text = text[2..];
        }
        if (text.Length != Characters)
        {
            return null;
        }
        var bytes = new byte[Bytes];
        for (var i = 0; i < Characters; i++)
        {
            var value = Alphabet.IndexOf(text[i], StringComparison.Ordinal);
            if (value < 0)
            {
                return null;
            }
            for (var b = 0; b < 5; b++)
            {
                var bit = i * 5 + b;
                var set = (value >> (4 - b) & 1) == 1;
                if (bit >= Bytes * 8)
                {
                    if (set)
                    {
                        return null; // the padding is always zero
                    }
                }
                else if (set)
                {
                    bytes[bit / 8] |= (byte)(0x80 >> bit % 8);
                }
            }
        }
        return bytes;
    }
}

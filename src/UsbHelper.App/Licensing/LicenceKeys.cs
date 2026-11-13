namespace UsbHelper.App.Licensing;

/// <summary>
/// The unlock code secrets of this build by keyId (#234). Never in the source: at build time the GenerateLicenceKeys
/// target in UsbHelper.App.csproj writes LicenceKeys.g.cs from the USBH_LICENCE_KEY_&lt;id&gt; variables, which CI sets
/// from its Actions secrets. Local and fork builds have none, so no code unlocks there.
/// </summary>
internal static partial class LicenceKeys
{
    public static IReadOnlyDictionary<int, byte[]> All { get; } = Load();

    private static Dictionary<int, byte[]> Load()
    {
        var keys = new Dictionary<int, byte[]>();
        AddKeys(keys);
        return keys;
    }

    static partial void AddKeys(Dictionary<int, byte[]> keys);
}

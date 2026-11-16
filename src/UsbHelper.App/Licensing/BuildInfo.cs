namespace UsbHelper.App.Licensing;

/// <summary>Licensing facts baked into this build at compile time.</summary>
public static class BuildInfo
{
    /// <summary>
    /// True when the build was made with USBH_FREE_UNLOCK=true (#238): the extra features are unlocked for everyone without
    /// a code. Only the beta and release publish steps set it, from the repository's Actions variable.
    /// </summary>
#if USBH_FREE_UNLOCK
    public const bool FreeUnlock = true;
#else
    public const bool FreeUnlock = false;
#endif
}

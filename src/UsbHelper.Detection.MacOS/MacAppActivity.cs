using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Keeps macOS from putting the app to sleep (App Nap) while it follows USB changes. A window that is covered or in the
/// background is napped: its timers and run loops are paused, so unplugging a device would only show once the window
/// is brought back. The activity still lets the display and the computer sleep.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacAppActivity : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSActivityUserInitiated without NSActivityIdleSystemSleepDisabled: not throttled, but the system may still sleep.
    private const ulong UserInitiatedAllowingIdleSleep = 0x00FFFFFFUL & ~(1UL << 20);

    private IntPtr _token;

    private MacAppActivity(IntPtr token) => _token = token;

    /// <summary>Starts the activity; null when the system does not provide it (the watcher then works as before).</summary>
    public static MacAppActivity? Begin(string reason)
    {
        try
        {
            var info = Send(GetClass("NSProcessInfo"), Selector("processInfo"));
            var text = Send(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), reason);
            var token = Send(info, Selector("beginActivityWithOptions:reason:"), UserInitiatedAllowingIdleSleep, text);
            if (token == IntPtr.Zero)
            {
                return null;
            }

            // The token must stay alive until endActivity.
            Send(token, Selector("retain"));
            return new MacAppActivity(token);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_token == IntPtr.Zero)
        {
            return;
        }

        var token = _token;
        _token = IntPtr.Zero;
        var info = Send(GetClass("NSProcessInfo"), Selector("processInfo"));
        Send(info, Selector("endActivity:"), token);
        Send(token, Selector("release"));
    }

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr target, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Send(IntPtr target, IntPtr selector, string argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr target, IntPtr selector, IntPtr argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr target, IntPtr selector, ulong options, IntPtr argument);
}

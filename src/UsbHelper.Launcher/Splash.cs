using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UsbHelper.Launcher;

/// <summary>
/// A small window with the app icon, the name, the version and a status line, drawn with plain Win32 calls so the
/// launcher needs no UI framework. <see cref="Run"/> owns the window and starts the work once it is shown; the work
/// uses <see cref="SetStatus"/>, <see cref="Ask"/> and <see cref="Close"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Splash
{
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SETFONT = 0x0030;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_APP_STATUS = 0x8000; // WM_APP
    private const uint WM_APP_ASK = 0x8001;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_TABSTOP = 0x00010000;
    private const int FirstChoice = 100; // control id of the first choice button
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_BORDER = 0x00800000;
    private const int Width = 360;
    private const int Height = 120;
    private const int IconSize = 64;
    private const uint Background = 0x00202020; // COLORREF, 0x00BBGGRR
    private const uint Foreground = 0x00FFFFFF;
    private const uint Dimmed = 0x00C0C0C0;

    private static nint window;
    private static nint icon;
    private static nint titleFont;
    private static nint textFont;
    private static string version = "";
    private static volatile string status = "";
    private static string question = "";
    private static IReadOnlyList<string> choices = [];
    private static readonly List<nint> buttons = [];
    private static int choice;
    private static readonly SemaphoreSlim answered = new(0);

    /// <summary>Shows the splash, runs the work on another thread, and returns when the work calls <see cref="Close"/>.</summary>
    public static void Run(string versionText, string statusText, Action work)
    {
        version = versionText;
        status = statusText;
        var instance = GetModuleHandleW(null);
        var scale = GetDpiForSystem() / 96.0;
        int Scaled(int value) => (int)Math.Round(value * scale);
        // The exe's own icon (ApplicationIcon), at the size it is drawn.
        nint large;
        if (SHDefExtractIconW(Environment.ProcessPath!, 0, 0, &large, null, (uint)Scaled(IconSize)) == 0)
        {
            icon = large;
        }
        titleFont = CreateFontW(-Scaled(22), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        textFont = CreateFontW(-Scaled(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        fixed (char* className = "UsbHelperSplash")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                hCursor = LoadCursorW(0, 32650), // IDC_APPSTARTING
                hbrBackground = CreateSolidBrush(Background),
                lpszClassName = className,
            };
            RegisterClassExW(&windowClass);
            int width = Scaled(Width), height = Scaled(Height);
            window = CreateWindowExW(0, className, "USB-helper", WS_POPUP | WS_VISIBLE | WS_BORDER,
                (GetSystemMetrics(0) - width) / 2, (GetSystemMetrics(1) - height) / 2, width, height, 0, 0, instance, 0);
        }
        if (window == 0)
        {
            work(); // No splash; SetStatus and Close then post to no window and do nothing.
            return;
        }
        // Only now: Close posts to the window, so the window must exist first.
        new Thread(() => work()).Start();

        MSG message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            // Tab between the choice buttons, Enter and Esc (see WindowProc).
            if (IsDialogMessageW(window, &message) == 0)
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
    }

    /// <summary>
    /// Shows the question and one button per choice under the status line, and returns the index of the clicked one.
    /// Esc, or closing the window, picks the last choice (Skip). Called from the work thread; without a window (none,
    /// or closed already) it returns the last choice at once.
    /// </summary>
    public static int Ask(string questionText, IReadOnlyList<string> choiceTexts)
    {
        question = questionText;
        choices = choiceTexts;
        // No window, or it was closed (Alt+F4) while the app was unpacked: nobody can answer.
        if (window == 0 || PostMessageW(window, WM_APP_ASK, 0, 0) == 0)
        {
            return choiceTexts.Count - 1;
        }
        answered.Wait();
        return choice;
    }

    public static void SetStatus(string text)
    {
        status = text;
        PostMessageW(window, WM_APP_STATUS, 0, 0);
    }

    public static void Close() => PostMessageW(window, WM_CLOSE, 0, 0);

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_PAINT:
                Paint(hwnd);
                return 0;
            case WM_APP_STATUS:
                InvalidateRect(hwnd, null, 1);
                return 0;
            case WM_APP_ASK:
                ShowChoices(hwnd);
                return 0;
            case WM_COMMAND when buttons.Count > 0:
                // A button's id, 1 (Enter: the focused button) or 2 (Esc: the last choice, Skip).
                var id = (int)(wParam & 0xFFFF);
                Answer(id switch
                {
                    1 => GetDlgCtrlID(GetFocus()) - FirstChoice,
                    2 => buttons.Count - 1,
                    _ => id - FirstChoice,
                });
                return 0;
            case WM_CLOSE when buttons.Count > 0:
                Answer(buttons.Count - 1);
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    // Grows the window and adds a button per choice, stacked, the first one focused.
    private static void ShowChoices(nint hwnd)
    {
        var scale = GetDpiForWindow(hwnd) / 96.0;
        int Scaled(int value) => (int)Math.Round(value * scale);
        int margin = Scaled(24), top = Scaled(Height + 32), rowHeight = Scaled(32), gap = Scaled(8);
        RECT area;
        GetWindowRect(hwnd, &area);
        var height = top + choices.Count * (rowHeight + gap) + margin - gap;
        SetWindowPos(hwnd, 0, area.left, area.top - (height - (area.bottom - area.top)) / 2, area.right - area.left, height, 0x0014); // SWP_NOZORDER | SWP_NOACTIVATE
        var instance = GetModuleHandleW(null);
        fixed (char* buttonClass = "BUTTON")
        {
            for (var i = 0; i < choices.Count; i++)
            {
                var button = CreateWindowExW(0, buttonClass, choices[i], WS_CHILD | WS_VISIBLE | WS_TABSTOP,
                    margin, top + i * (rowHeight + gap), area.right - area.left - 2 * margin, rowHeight, hwnd, FirstChoice + i, instance, 0);
                SendMessageW(button, WM_SETFONT, textFont, 1);
                buttons.Add(button);
            }
        }
        SetForegroundWindow(hwnd);
        SetFocus(buttons[0]);
        InvalidateRect(hwnd, null, 1);
    }

    private static void Answer(int index)
    {
        if (index < 0 || index >= buttons.Count)
        {
            return;
        }
        foreach (var button in buttons)
        {
            DestroyWindow(button);
        }
        buttons.Clear();
        question = "";
        InvalidateRect(window, null, 1);
        choice = index;
        answered.Release();
    }

    private static void Paint(nint hwnd)
    {
        PAINTSTRUCT paint;
        var dc = BeginPaint(hwnd, &paint);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        int Scaled(int value) => (int)Math.Round(value * scale);
        int margin = Scaled(24), iconSize = Scaled(IconSize);
        if (icon != 0)
        {
            DrawIconEx(dc, margin, (Scaled(Height) - iconSize) / 2, icon, iconSize, iconSize, 0, 0, 3); // DI_NORMAL
        }
        SetBkMode(dc, 1); // TRANSPARENT
        var left = margin * 2 + iconSize;
        Text(dc, titleFont, Foreground, "USB-helper", left, Scaled(22), Scaled(Width) - margin);
        Text(dc, textFont, Dimmed, version, left, Scaled(54), Scaled(Width) - margin);
        Text(dc, textFont, Foreground, status, left, Scaled(76), Scaled(Width) - margin);
        if (question.Length > 0)
        {
            Text(dc, textFont, Foreground, question, margin, Scaled(Height), Scaled(Width) - margin);
        }
        EndPaint(hwnd, &paint);
    }

    private static void Text(nint dc, nint font, uint color, string text, int left, int top, int right)
    {
        SelectObject(dc, font);
        SetTextColor(dc, color);
        var area = new RECT { left = left, top = top, right = right, bottom = top + 40 };
        DrawTextW(dc, text, text.Length, &area, 0x8020); // DT_SINGLELINE | DT_END_ELLIPSIS
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(MSG* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    private static partial int TranslateMessage(MSG* message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(MSG* message);

    [LibraryImport("user32.dll")]
    private static partial int PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int InvalidateRect(nint hwnd, RECT* rect, int erase);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial int IsDialogMessageW(nint dialog, MSG* message);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    private static partial int GetDlgCtrlID(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* paint);

    [LibraryImport("user32.dll")]
    private static partial int EndPaint(nint hwnd, PAINTSTRUCT* paint);

    [LibraryImport("user32.dll")]
    private static partial int DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawTextW(nint dc, string text, int length, RECT* rect, uint format);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIconW(string iconFile, int index, uint flags, nint* large, nint* small, uint size);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint dc, uint color);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint dc, int mode);
}

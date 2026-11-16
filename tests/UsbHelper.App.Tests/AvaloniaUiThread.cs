using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace UsbHelper.App.Tests;

/// <summary>
/// Gives the test run an Avalonia UI thread that runs its dispatcher, as the app's own main thread does (#245). Without a
/// platform, the UI thread is whichever thread first touches Avalonia, here a test thread that never runs its dispatcher.
/// A NativeMenuItem's Command is checked on the UI thread: set on any other thread, it waited for that test thread
/// forever and hung the whole run. So tests that build native menu items with commands run on this thread, with Run.
/// </summary>
internal static class AvaloniaUiThread
{
    /// <summary>Runs the test's code on the UI thread and waits for it; its exceptions (and failed asserts) come back.</summary>
    public static void Run(Action test) => Dispatcher.UIThread.Invoke(test);

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255", Justification = "A test assembly: the UI thread must exist before any test runs.")]
    internal static void Start()
    {
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.UIThread; // The first thread to touch Avalonia becomes its UI thread.
            ready.Set();
            dispatcher.MainLoop(CancellationToken.None);
        })
        { IsBackground = true, Name = "Avalonia UI thread (tests)" };
        thread.Start();
        ready.Wait();
        if (Dispatcher.UIThread.Thread != thread)
        {
            throw new InvalidOperationException("Another thread became Avalonia's UI thread before the tests' one.");
        }
    }
}

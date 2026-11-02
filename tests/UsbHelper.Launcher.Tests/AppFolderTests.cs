using System.IO.Compression;
using System.Text;

namespace UsbHelper.Launcher.Tests;

/// <summary>Unpacking the app carried by the launcher to %LOCALAPPDATA%\USB-helper\&lt;version&gt; (#110).</summary>
public sealed class AppFolderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "usbhelper-launcher-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly Dictionary<string, string> AppFiles = new()
    {
        ["UsbHelper.App.exe"] = "the app",
        ["libSkiaSharp.dll"] = new string('s', 100_000),
    };

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Payload(IReadOnlyDictionary<string, string> files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return memory.ToArray();
    }

    // Counts how often the launcher opens its payload: a reused folder must not open it.
    private sealed class PayloadSource(byte[] bytes)
    {
        public int Opened { get; private set; }

        public Stream Open()
        {
            Opened++;
            return new MemoryStream(bytes);
        }
    }

    private static void AssertHoldsTheApp(string folder)
    {
        foreach (var (name, text) in AppFiles)
        {
            Assert.Equal(text, File.ReadAllText(Path.Combine(folder, name)));
        }
        Assert.True(AppFolder.IsComplete(folder));
    }

    [Fact]
    public void FirstStartUnpacksTheAppIntoTheVersionFolder()
    {
        var payload = new PayloadSource(Payload(AppFiles));

        var folder = AppFolder.Prepare(root, "1.2.0-beta.3", payload.Open);

        Assert.Equal(Path.Combine(root, "1.2.0-beta.3"), folder);
        AssertHoldsTheApp(folder);
        Assert.Equal(1, payload.Opened);
        // Nothing else is left in the root, such as the folder it unpacked into.
        Assert.Equal([folder], Directory.GetDirectories(root));
    }

    [Fact]
    public void LaterStartsReuseACompleteUnpack()
    {
        var payload = new PayloadSource(Payload(AppFiles));
        var folder = AppFolder.Prepare(root, "1.2.0", payload.Open);
        var written = File.GetLastWriteTimeUtc(Path.Combine(folder, "UsbHelper.App.exe"));

        Assert.True(AppFolder.IsComplete(folder));
        Assert.Equal(folder, AppFolder.Prepare(root, "1.2.0", payload.Open));

        Assert.Equal(1, payload.Opened);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(folder, "UsbHelper.App.exe")));
    }

    [Fact]
    public void AFolderWithoutTheCompleteMarkIsUnpackedAgain()
    {
        // An unpack that stopped half way: one file, no mark.
        var folder = Path.Combine(root, "1.2.0");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "UsbHelper.App.exe"), "the a");
        Assert.False(AppFolder.IsComplete(folder));

        var payload = new PayloadSource(Payload(AppFiles));
        Assert.Equal(folder, AppFolder.Prepare(root, "1.2.0", payload.Open));

        AssertHoldsTheApp(folder);
        Assert.Equal(1, payload.Opened);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("shorter")]
    public void AFolderWithAMissingOrChangedFileIsUnpackedAgain(string damage)
    {
        var payload = new PayloadSource(Payload(AppFiles));
        var folder = AppFolder.Prepare(root, "1.2.0", payload.Open);
        var dll = Path.Combine(folder, "libSkiaSharp.dll");
        if (damage == "missing")
        {
            File.Delete(dll);
        }
        else
        {
            File.WriteAllText(dll, "sss");
        }
        Assert.False(AppFolder.IsComplete(folder));

        Assert.Equal(folder, AppFolder.Prepare(root, "1.2.0", payload.Open));

        AssertHoldsTheApp(folder);
        Assert.Equal(2, payload.Opened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a mark")]
    [InlineData("abc twelve UsbHelper.App.exe")]
    public void AnEmptyOrDamagedMarkIsNotComplete(string mark)
    {
        var folder = AppFolder.Prepare(root, "1.2.0", new PayloadSource(Payload(AppFiles)).Open);
        File.WriteAllText(Path.Combine(folder, ".complete"), mark);

        Assert.False(AppFolder.IsComplete(folder));

        var payload = new PayloadSource(Payload(AppFiles));
        AppFolder.Prepare(root, "1.2.0", payload.Open);
        AssertHoldsTheApp(folder);
        Assert.Equal(1, payload.Opened);
    }

    [Fact]
    public void AnUnpackThatAnotherLauncherFinishedFirstKeepsTheOtherOne()
    {
        // A double-click on the first start: the other launcher completes the folder (and starts the app from it) while
        // this one is still unpacking. Its folder must not be deleted or replaced.
        string? other = null;
        var payload = new PayloadSource(Payload(AppFiles));
        Stream OpenAfterTheOtherFinished()
        {
            other = AppFolder.Prepare(root, "1.2.0", payload.Open);
            File.WriteAllText(Path.Combine(other, "in use"), "");
            return payload.Open();
        }

        var folder = AppFolder.Prepare(root, "1.2.0", OpenAfterTheOtherFinished);

        Assert.Equal(other, folder);
        Assert.True(File.Exists(Path.Combine(folder, "in use")));
        AssertHoldsTheApp(folder);
        Assert.Equal([folder], Directory.GetDirectories(root));
    }

    [Fact]
    public void ABrokenPayloadIsRejectedAndLeavesNothingBehind()
    {
        var bytes = Payload(AppFiles);
        // Damage the compressed data of the first file, not the zip directory at the end.
        for (var i = 40; i < 60; i++)
        {
            bytes[i] ^= 0xFF;
        }
        var payload = new PayloadSource(bytes);

        Assert.ThrowsAny<InvalidDataException>(() => AppFolder.Prepare(root, "1.2.0", payload.Open));

        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void OtherVersionsAndTheirLeftoversAreRemoved()
    {
        var current = AppFolder.Prepare(root, "1.2.0", new PayloadSource(Payload(AppFiles)).Open);
        foreach (var other in new[] { "1.1.0", "1.2.0-beta.4", "1.3.0", "1.1.0.unpacking-0123" })
        {
            Directory.CreateDirectory(Path.Combine(root, other));
            File.WriteAllText(Path.Combine(root, other, "UsbHelper.App.exe"), "old");
        }
        // Another launcher of this version may be unpacking right now (a double-click): its folder stays.
        var unpacking = Directory.CreateDirectory(Path.Combine(root, "1.2.0.unpacking-4567")).FullName;

        AppFolder.RemoveOtherVersions(root, "1.2.0");

        Assert.Equal([current, unpacking], Directory.GetDirectories(root).Order());
        AssertHoldsTheApp(current);
    }

    [Fact]
    public void RemovingOtherVersionsWithoutARootDoesNothing()
    {
        AppFolder.RemoveOtherVersions(root, "1.2.0");

        Assert.False(Directory.Exists(root));
    }
}

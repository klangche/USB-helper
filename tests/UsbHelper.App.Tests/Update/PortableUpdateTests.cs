using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UsbHelper.App.Update;

namespace UsbHelper.App.Tests.Update;

/// <summary>Downloading, verifying and unzipping a release next to the running portable app (#123).</summary>
public sealed class PortableUpdateTests : IDisposable
{
    private readonly string parent = Directory.CreateTempSubdirectory("usbhelper-update-tests-").FullName;
    private readonly string app;

    private static readonly Release Release = new(
        "0.7.0",
        new Uri("https://github.com/klangche/USB-helper/releases/download/v0.7.0/usb-helper-0.7.0-windows-x64.zip"),
        new Uri("https://github.com/klangche/USB-helper/releases/download/v0.7.0/usb-helper-0.7.0-windows-x64.zip.sha256"));

    public PortableUpdateTests()
    {
        // The running app's top folder, as the user unzipped it (#130): USB-helper.exe and the app folder.
        app = Path.Combine(parent, "USB-helper");
        Directory.CreateDirectory(Path.Combine(app, "app"));
        File.WriteAllText(Path.Combine(app, PortableUpdate.StarterName), "starter 0.6.0");
    }

    public void Dispose() => Directory.Delete(parent, recursive: true);

    // A release zip as release.yml makes it (#130): one USB-helper folder with USB-helper.exe, the text files and the
    // app folder with the app's exe and DLLs.
    private static byte[] ReleaseZip(params (string Path, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files.Length > 0 ? files : Layout)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return memory.ToArray();
    }

    private static readonly (string, string)[] Layout =
    [
        ("USB-helper/USB-helper.exe", "starter 0.7.0"),
        ("USB-helper/READ_ME_FIRST.txt", "read me"),
        ("USB-helper/app/UsbHelper.exe", "app 0.7.0"),
        ("USB-helper/app/Avalonia.Base.dll", "a dll"),
    ];

    // The checksum file as release.yml writes it (sha256sum format).
    private static byte[] Checksum(byte[] zip) =>
        Encoding.ASCII.GetBytes($"{Convert.ToHexStringLower(SHA256.HashData(zip))}  usb-helper-0.7.0-windows-x64.zip\n");

    private static FakeHttp Serving(byte[] zip, byte[] checksum) => FakeHttp.Serving(new Dictionary<string, byte[]>
    {
        [Release.Download.ToString()] = zip,
        [Release.Checksum.ToString()] = checksum,
    });

    private string[] Folders() => [.. Directory.GetDirectories(parent).Select(Path.GetFileName).Order()!];

    [Fact]
    public async Task AVerifiedReleaseIsUnzippedIntoANewFolderNextToTheApp()
    {
        var zip = ReleaseZip();
        var progress = new List<int>();

        var exe = await PortableUpdate.DownloadAsync(Serving(zip, Checksum(zip)).Client(), Release, app, progress.Add, CancellationToken.None);

        // The new version's USB-helper.exe is started; it starts the app in its own app folder.
        var target = Path.Combine(parent, "USB-helper-0.7.0");
        Assert.Equal(Path.Combine(target, "USB-helper.exe"), exe);
        Assert.Equal("starter 0.7.0", File.ReadAllText(exe));
        Assert.Equal("app 0.7.0", File.ReadAllText(Path.Combine(target, "app", "UsbHelper.exe")));
        Assert.Equal("a dll", File.ReadAllText(Path.Combine(target, "app", "Avalonia.Base.dll")));
        Assert.Equal("read me", File.ReadAllText(Path.Combine(target, "READ_ME_FIRST.txt")));
        // The running version stays as it is, for the user to delete.
        Assert.Equal("starter 0.6.0", File.ReadAllText(Path.Combine(app, "USB-helper.exe")));
        Assert.Equal(["USB-helper", "USB-helper-0.7.0"], Folders());
        Assert.Equal(100, progress[^1]);
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public async Task AnExistingFolderOfThatVersionIsKeptAndANumberedOneIsUsed()
    {
        Directory.CreateDirectory(Path.Combine(parent, "USB-helper-0.7.0"));
        File.WriteAllText(Path.Combine(parent, "USB-helper-0.7.0", "mine.txt"), "the user's");
        var zip = ReleaseZip();

        var exe = await PortableUpdate.DownloadAsync(Serving(zip, Checksum(zip)).Client(), Release, app, null, CancellationToken.None);

        Assert.Equal(Path.Combine(parent, "USB-helper-0.7.0 (2)", "USB-helper.exe"), exe);
        Assert.Equal("the user's", File.ReadAllText(Path.Combine(parent, "USB-helper-0.7.0", "mine.txt")));
    }

    [Fact]
    public async Task ABadChecksumIsRejectedAndNothingIsWritten()
    {
        var zip = ReleaseZip();
        var other = Checksum(ReleaseZip(("USB-helper/USB-helper.exe", "changed")));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PortableUpdate.DownloadAsync(Serving(zip, other).Client(), Release, app, null, CancellationToken.None));

        Assert.Equal(["USB-helper"], Folders());
        Assert.Empty(Directory.GetFiles(parent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a checksum")]
    public async Task AnUnreadableChecksumIsRejected(string checksum)
    {
        var zip = ReleaseZip();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PortableUpdate.DownloadAsync(Serving(zip, Encoding.ASCII.GetBytes(checksum)).Client(), Release, app, null, CancellationToken.None));

        Assert.Equal(["USB-helper"], Folders());
    }

    [Theory]
    [InlineData("usb-helper-0.5.0-windows-x64/USB-helper.exe", "")] // a 0.5.0 zip: one launcher exe
    [InlineData("USB-helper/UsbHelper.exe", "")] // the #123 layout: the app's exe at the top, no app folder
    [InlineData("USB-helper/USB-helper.exe", "")] // the starter without the app
    [InlineData("USB-helper/app/UsbHelper.exe", "")] // the app without the starter
    [InlineData("Other/USB-helper.exe", "Other/app/UsbHelper.exe")]
    public async Task AZipWithoutTheStarterAndTheAppIsRejected(string first, string second)
    {
        var zip = ReleaseZip([.. new[] { first, second }.Where(path => path.Length > 0).Select(path => (path, "x"))]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PortableUpdate.DownloadAsync(Serving(zip, Checksum(zip)).Client(), Release, app, null, CancellationToken.None));

        Assert.Equal(["USB-helper"], Folders());
        Assert.Empty(Directory.GetFiles(parent));
    }

    [Fact]
    public async Task AFailedDownloadIsReported()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            PortableUpdate.DownloadAsync(FakeHttp.Serving(new Dictionary<string, byte[]>()).Client(), Release, app, null, CancellationToken.None));

        Assert.Equal(["USB-helper"], Folders());
    }
}

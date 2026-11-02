using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace UsbHelper.Launcher.Tests;

/// <summary>Downloading, verifying and putting a new launcher in place (#111).</summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-update-tests-").FullName;
    private readonly string exe;

    private static readonly Release Release = new(
        "0.7.0",
        new Uri("https://github.com/klangche/USB-helper/releases/download/v0.7.0/usb-helper-0.7.0-windows-x64.zip"),
        new Uri("https://github.com/klangche/USB-helper/releases/download/v0.7.0/usb-helper-0.7.0-windows-x64.zip.sha256"));

    public UpdateInstallerTests()
    {
        // The launcher as the user saved it, under any name.
        exe = Path.Combine(folder, "My USB-helper.exe");
        File.WriteAllText(exe, "launcher 0.6.0");
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // A release zip as release.yml makes it: one folder with the launcher and the text files.
    private static byte[] ReleaseZip(string launcher = "launcher 0.7.0", string name = "USB-helper.exe")
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in new[] { ($"usb-helper-0.7.0-windows-x64/{name}", launcher), ("usb-helper-0.7.0-windows-x64/READ_ME_FIRST.txt", "read me") })
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return memory.ToArray();
    }

    // The checksum file as release.yml writes it (sha256sum format).
    private static byte[] Checksum(byte[] zip) =>
        Encoding.ASCII.GetBytes($"{Convert.ToHexStringLower(SHA256.HashData(zip))}  usb-helper-0.7.0-windows-x64.zip\n");

    private static FakeHttp Serving(byte[] zip, byte[] checksum) => FakeHttp.Serving(new Dictionary<string, byte[]>
    {
        [Release.Zip.ToString()] = zip,
        [Release.Checksum.ToString()] = checksum,
    });

    [Fact]
    public async Task AVerifiedDownloadIsWrittenNextToTheLauncher()
    {
        var zip = ReleaseZip();
        var progress = new List<int>();

        var downloaded = await UpdateInstaller.DownloadAsync(Serving(zip, Checksum(zip)).Client(), Release, exe, progress.Add, CancellationToken.None);

        Assert.Equal(exe + ".new", downloaded);
        Assert.Equal("launcher 0.7.0", File.ReadAllText(downloaded));
        Assert.Equal("launcher 0.6.0", File.ReadAllText(exe));
        Assert.Equal(100, progress[^1]);
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public async Task ABadChecksumIsRejectedAndNothingIsWritten()
    {
        var zip = ReleaseZip();
        var other = Checksum(ReleaseZip("launcher 0.7.0, changed"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.DownloadAsync(Serving(zip, other).Client(), Release, exe, null, CancellationToken.None));

        Assert.Equal([exe], Directory.GetFiles(folder));
        Assert.Equal("launcher 0.6.0", File.ReadAllText(exe));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a checksum")]
    public async Task AnUnreadableChecksumIsRejected(string checksum)
    {
        var zip = ReleaseZip();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.DownloadAsync(Serving(zip, Encoding.ASCII.GetBytes(checksum)).Client(), Release, exe, null, CancellationToken.None));

        Assert.Equal([exe], Directory.GetFiles(folder));
    }

    [Fact]
    public async Task AZipWithoutTheLauncherIsRejected()
    {
        var zip = ReleaseZip(name: "Something.exe");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateInstaller.DownloadAsync(Serving(zip, Checksum(zip)).Client(), Release, exe, null, CancellationToken.None));

        Assert.Equal([exe], Directory.GetFiles(folder));
    }

    [Fact]
    public async Task AFailedDownloadIsReported()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            UpdateInstaller.DownloadAsync(FakeHttp.Serving(new Dictionary<string, byte[]>()).Client(), Release, exe, null, CancellationToken.None));

        Assert.Equal([exe], Directory.GetFiles(folder));
    }

    [Fact]
    public void ReplaceKeepsTheOldLauncherAsOldUntilTheNextStart()
    {
        var downloaded = exe + ".new";
        File.WriteAllText(downloaded, "launcher 0.7.0");

        UpdateInstaller.Replace(exe, downloaded);

        // Windows lets a running exe be renamed but not deleted: the old one moves aside.
        Assert.Equal("launcher 0.7.0", File.ReadAllText(exe));
        Assert.Equal("launcher 0.6.0", File.ReadAllText(exe + ".old"));
        Assert.False(File.Exists(downloaded));

        UpdateInstaller.RemoveOld(exe);

        Assert.Equal([exe], Directory.GetFiles(folder));
    }

    [Fact]
    public void AFailedReplacePutsTheRunningLauncherBack()
    {
        // The new launcher is gone (for example removed by a virus scanner) before it is moved into place.
        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Replace(exe, exe + ".new"));

        Assert.Equal("launcher 0.6.0", File.ReadAllText(exe));
        Assert.Equal([exe], Directory.GetFiles(folder));
    }

    [Fact]
    public void ReplaceOverwritesAnOldLauncherLeftFromAnEarlierUpdate()
    {
        File.WriteAllText(exe + ".old", "launcher 0.5.0");
        File.WriteAllText(exe + ".new", "launcher 0.7.0");

        UpdateInstaller.Replace(exe, exe + ".new");

        Assert.Equal("launcher 0.7.0", File.ReadAllText(exe));
        Assert.Equal("launcher 0.6.0", File.ReadAllText(exe + ".old"));
    }

    [Fact]
    public void RemoveOldWithoutAnOldLauncherDoesNothing()
    {
        UpdateInstaller.RemoveOld(exe);

        Assert.Equal([exe], Directory.GetFiles(folder));
    }
}

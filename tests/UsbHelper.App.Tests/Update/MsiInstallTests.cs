using UsbHelper.App.Update;

namespace UsbHelper.App.Tests.Update;

/// <summary>Handing a downloaded .msi to Windows Installer so the new version starts when it is installed (#147).</summary>
public class MsiInstallTests
{
    [Fact]
    public void TheMsiRunsWithProgressNoRestartAndLaunchesTheNewVersion()
    {
        var start = MsiInstall.StartInfo(@"C:\Temp\USB-helper-update\usb-helper-0.7.0-windows-x64.msi");

        Assert.Equal("msiexec.exe", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal(
            ["/i", @"C:\Temp\USB-helper-update\usb-helper-0.7.0-windows-x64.msi", "/passive", "/norestart", "LAUNCHAPPONEXIT=1"],
            start.ArgumentList);
    }

    [Fact]
    public void TheLaunchPropertyIsTheOneThePackageReads()
    {
        // installer/Package.wxs starts USB-helper.exe after InstallFinalize when this property is 1.
        var package = File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "Package.wxs"));

        Assert.Contains($"Condition=\"{MsiInstall.LaunchProperty} = 1 AND NOT REMOVE\"", package);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UsbHelper.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("The repository root is not above the test folder.");
    }
}

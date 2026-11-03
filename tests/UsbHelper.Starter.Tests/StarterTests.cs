namespace UsbHelper.Starter.Tests;

/// <summary>USB-helper.exe at the top of the zip and the install folder starts app\UsbHelper.exe (#130).</summary>
public sealed class StarterTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("usbhelper-starter-tests-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void TheAppIsInTheAppFolderNextToTheStarter()
    {
        Assert.Equal(Path.Combine(folder, "app", "UsbHelper.exe"), Starter.AppPath(folder));
    }

    [Fact]
    public void TheAppGetsTheStartersArgumentsAndWorkingFolder()
    {
        var start = Starter.StartInfo(Path.Combine(folder, "app", "UsbHelper.exe"), ["--first", "a b"], @"C:\Work");

        Assert.Equal(Path.Combine(folder, "app", "UsbHelper.exe"), start.FileName);
        Assert.Equal(["--first", "a b"], start.ArgumentList);
        Assert.Equal(@"C:\Work", start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void WithoutTheAppFolderTheStarterSaysWhatToDo()
    {
        // For example USB-helper.exe copied alone to the Desktop.
        var error = Starter.Check(folder);

        Assert.NotNull(error);
        Assert.Contains(Path.Combine(folder, "app", "UsbHelper.exe"), error);
        Assert.Contains("https://github.com/klangche/USB-helper/releases", error);
    }

    [Fact]
    public void WithTheAppFolderThereIsNothingToSay()
    {
        Directory.CreateDirectory(Path.Combine(folder, "app"));
        File.WriteAllText(Path.Combine(folder, "app", "UsbHelper.exe"), "the app");

        Assert.Null(Starter.Check(folder));
    }
}

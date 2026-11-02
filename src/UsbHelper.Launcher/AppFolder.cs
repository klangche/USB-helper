using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

namespace UsbHelper.Launcher;

/// <summary>
/// The app the launcher carries, unpacked once per version to &lt;root&gt;\&lt;version&gt; (#110). A folder counts as
/// complete only with its mark, written after every file was checked against the payload by its SHA-256.
/// </summary>
public static class AppFolder
{
    private const string Mark = ".complete";

    /// <summary>The version's folder, unpacked from the payload first unless a complete one is there.</summary>
    public static string Prepare(string root, string version, Func<Stream> openPayload)
    {
        var folder = Path.Combine(root, version);
        if (IsComplete(folder))
        {
            return folder;
        }
        // Unpacked next to the final folder and moved there when complete, so a stopped unpack never looks finished.
        var unpacking = Path.Combine(root, $"{version}.unpacking-{Guid.NewGuid():N}");
        try
        {
            Unpack(openPayload, unpacking);
            // Another launcher of this version may have finished first and started the app from its folder.
            if (IsComplete(folder))
            {
                return folder;
            }
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            Directory.Move(unpacking, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && IsComplete(folder))
        {
            // Another launcher of this version finished first, between the check above and the move.
        }
        finally
        {
            if (Directory.Exists(unpacking))
            {
                Directory.Delete(unpacking, recursive: true);
            }
        }
        return folder;
    }

    /// <summary>
    /// The mark is there, lists at least one file, and every file it lists has its size. Cheap: no file is read. A
    /// damaged mark (for example empty after a power cut) counts as not complete.
    /// </summary>
    public static bool IsComplete(string folder)
    {
        var mark = Path.Combine(folder, Mark);
        if (!File.Exists(mark))
        {
            return false;
        }
        var lines = File.ReadAllLines(mark);
        foreach (var line in lines)
        {
            // <sha256> <size> <path>
            var parts = line.Split(' ', 3);
            if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                return false;
            }
            var file = new FileInfo(Path.Combine(folder, parts[2]));
            if (!file.Exists || file.Length != size)
            {
                return false;
            }
        }
        return lines.Length > 0;
    }

    /// <summary>
    /// Removes the folders of every other version (older or newer: only the running one is needed) and their stopped
    /// unpacks. This version's unpacks stay, as another launcher of it may be unpacking right now; the next start of it
    /// removes them. What cannot be removed, such as the files of an older copy of the app that is still open, stays
    /// until a later start.
    /// </summary>
    public static void RemoveOtherVersions(string root, string version)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        foreach (var other in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(other);
            if (name == version || name.StartsWith($"{version}.unpacking-", StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                Directory.Delete(other, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Unpack(Func<Stream> openPayload, string folder)
    {
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        using (var payload = openPayload())
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue; // a folder
                }
                var path = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var sha256 = SHA256.Create();
                using (var hashing = new CryptoStream(entry.Open(), sha256, CryptoStreamMode.Read))
                using (var target = File.Create(path))
                {
                    hashing.CopyTo(target);
                }
                var expected = sha256.Hash!;
                using (var written = File.OpenRead(path))
                {
                    if (!SHA256.HashData(written).AsSpan().SequenceEqual(expected))
                    {
                        throw new InvalidDataException($"{entry.FullName} was not written completely.");
                    }
                }
                lines.Add($"{Convert.ToHexStringLower(expected)} {entry.Length.ToString(CultureInfo.InvariantCulture)} {entry.FullName}");
            }
        }
        // On disk before the folder is moved into place, so a power cut cannot leave a complete folder with an empty mark.
        using var mark = new FileStream(Path.Combine(folder, Mark), FileMode.Create);
        using (var writer = new StreamWriter(mark, leaveOpen: true))
        {
            writer.Write(string.Join('\n', lines));
        }
        mark.Flush(flushToDisk: true);
    }
}

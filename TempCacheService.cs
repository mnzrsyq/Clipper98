namespace Clipper98;

public sealed record StorageSummary(long Bytes, int Files, int Folders)
{
    public string FormattedSize => Bytes < 1024 * 1024
        ? $"{Bytes / 1024.0:0.0} KB"
        : $"{Bytes / 1024.0 / 1024.0:0.0} MB";
}

public sealed record CleanupSummary(long RemovedBytes, int RemovedFiles, int RemovedFolders, int SkippedFolders);

/// <summary>Reads and removes only Clipper-owned transient folders, never outputs, models, or settings.</summary>
public static class TempCacheService
{
    public static IReadOnlyList<string> Roots
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return
            [
                Path.Combine(Path.GetTempPath(), "Clipper98"),
                Path.Combine(local, "Clipper98", "Cache")
            ];
        }
    }

    public static StorageSummary GetSummary()
    {
        long bytes = 0;
        var files = 0;
        var folders = 0;
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            Accumulate(root, ref bytes, ref files, ref folders);
        }
        return new StorageSummary(bytes, files, folders);
    }

    public static CleanupSummary Clear()
    {
        long removedBytes = 0;
        var removedFiles = 0;
        var removedFolders = 0;
        var skippedFolders = 0;

        foreach (var configuredRoot in Roots)
        {
            var root = Path.GetFullPath(configuredRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root) || !IsAllowedRoot(root)) continue;

            // A cache lock protects the whole persistent cache, so never clear its sibling entries mid-render.
            if (ContainsActiveJob(root))
            {
                skippedFolders++;
                continue;
            }

            foreach (var child in Directory.EnumerateFileSystemEntries(root).ToList())
            {
                var fullChild = Path.GetFullPath(child);
                if (!IsChildOf(fullChild, root)) continue;
                var attributes = File.GetAttributes(fullChild);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedFolders++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (ContainsActiveJob(fullChild) || ContainsReparsePoint(fullChild))
                    {
                        skippedFolders++;
                        continue;
                    }

                    var summary = Measure(fullChild);
                    Directory.Delete(fullChild, recursive: true);
                    removedBytes += summary.Bytes;
                    removedFiles += summary.Files;
                    removedFolders += summary.Folders + 1;
                }
                else
                {
                    var size = new FileInfo(fullChild).Length;
                    File.Delete(fullChild);
                    removedBytes += size;
                    removedFiles++;
                }
            }
        }

        return new CleanupSummary(removedBytes, removedFiles, removedFolders, skippedFolders);
    }

    public static FileStream AcquireActiveLock(string folder)
    {
        Directory.CreateDirectory(folder);
        return new FileStream(Path.Combine(folder, ".active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static bool IsAllowedRoot(string root)
    {
        var allowed = Roots.Select(Path.GetFullPath)
            .Any(candidate => string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase));
        return allowed;
    }

    private static bool IsChildOf(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsActiveJob(string folder)
    {
        foreach (var lockPath in Directory.EnumerateFiles(folder, ".active.lock", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsReparsePoint(string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static (long Bytes, int Files, int Folders) Measure(string folder)
    {
        long bytes = 0;
        var files = 0;
        var folders = 0;
        Accumulate(folder, ref bytes, ref files, ref folders);
        return (bytes, files, folders);
    }

    private static void Accumulate(string folder, ref long bytes, ref int files, ref int folders)
    {
        try
        {
            folders++;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                bytes += new FileInfo(file).Length;
                files++;
            }
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                Accumulate(child, ref bytes, ref files, ref folders);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

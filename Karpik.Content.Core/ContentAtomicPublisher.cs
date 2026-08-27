namespace Karpik.Content.Core;

public sealed class ContentAtomicPublisher
{
    private readonly string _outputRoot;
    private readonly string _stagingRoot;

    public ContentAtomicPublisher(string outputRoot)
    {
        _outputRoot = Path.GetFullPath(outputRoot);
        _stagingRoot = Path.Combine(_outputRoot, ".staging");
    }

    public void Publish(string stagingDirectory)
    {
        string stagingFull = Path.GetFullPath(stagingDirectory);
        if (!PathSafety.IsContained(_stagingRoot, stagingFull) && !string.Equals(stagingFull.TrimEnd(Path.DirectorySeparatorChar), _stagingRoot.TrimEnd(Path.DirectorySeparatorChar), PathSafety.PathComparison))
        {
            if (!PathSafety.IsContained(_stagingRoot, stagingFull))
            {
                throw new ArgumentException($"Staging directory must be under {_stagingRoot}", nameof(stagingDirectory));
            }
        }

        if (!Directory.Exists(stagingFull))
        {
            throw new DirectoryNotFoundException($"Staging directory not found: {stagingFull}");
        }

        Directory.CreateDirectory(_outputRoot);

        string stagingManifest = Path.Combine(stagingFull, "manifest.json");
        if (!File.Exists(stagingManifest))
        {
            throw new InvalidDataException($"Staging manifest missing: {stagingManifest}");
        }

        string replacementRoot = Path.Combine(_outputRoot, ".replacement");
        Directory.CreateDirectory(replacementRoot);
        string backupDir = Path.Combine(replacementRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var publishedEntries = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(_outputRoot))
        {
            string name = Path.GetFileName(entry);
            if (name == ".staging" || name == ".replacement") continue;
            publishedEntries.Add(entry);
        }

        try
        {
            // Move existing published entries to backup (preserve structure)
            foreach (string entry in publishedEntries)
            {
                string dest = Path.Combine(backupDir, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, dest);
                }
                else if (File.Exists(entry))
                {
                    File.Move(entry, dest);
                }
            }

            // Build map of backup files (relative -> full path)
            var backupFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(backupDir))
            {
                foreach (string file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(backupDir, file).Replace(Path.DirectorySeparatorChar, '/');
                    backupFiles[rel] = file;
                }
            }

            // Build map of staging files
            var stagingFiles = new List<string>();
            foreach (string file in Directory.EnumerateFiles(stagingFull, "*", SearchOption.AllDirectories))
            {
                stagingFiles.Add(file);
            }

            var stagedRelativeSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stagingFile in stagingFiles)
            {
                string rel = Path.GetRelativePath(stagingFull, stagingFile).Replace(Path.DirectorySeparatorChar, '/');
                stagedRelativeSet.Add(rel);
                string dest = Path.Combine(_outputRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string? destDir = Path.GetDirectoryName(dest);
                if (destDir is not null) Directory.CreateDirectory(destDir);

                // If backup has same relative path and content equal, reuse backup (preserve timestamp, no rewrite)
                if (backupFiles.TryGetValue(rel, out string? backupFile) && FilesEqual(backupFile, stagingFile))
                {
                    // Move from backup to output, discard staging file
                    File.Move(backupFile, dest);
                    File.Delete(stagingFile);
                    backupFiles.Remove(rel);
                }
                else
                {
                    // Move staging file to output (overwrites if backup file exists but not equal, we already removed backup entry? Need to delete backup if exists)
                    if (backupFiles.TryGetValue(rel, out string? existingBackup))
                    {
                        // Backup file is not equal, delete it (it will be GC'd as stale)
                        File.Delete(existingBackup);
                        backupFiles.Remove(rel);
                    }

                    // Ensure dest not exists
                    if (File.Exists(dest))
                    {
                        File.Delete(dest);
                    }
                    File.Move(stagingFile, dest);
                }
            }

            // Also handle staging directories that may be empty? But files already moved, need to clean staging dir
            // Delete remaining backup files that are stale (not in new staging) - they are already in backupDir but not moved, just delete them
            foreach (var kv in backupFiles)
            {
                try { File.Delete(kv.Value); } catch { }
            }

            // Clean up empty backup directories
            TryDeleteEmptyDirectories(backupDir);

            // Delete staging directory
            if (Directory.Exists(stagingFull))
            {
                Directory.Delete(stagingFull, recursive: true);
            }

            // Delete backup dir if empty
            if (Directory.Exists(backupDir))
            {
                // If still has directories (e.g., artifacts), delete recursively
                Directory.Delete(backupDir, recursive: true);
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(replacementRoot).Any())
                {
                    Directory.Delete(replacementRoot);
                }
            }
            catch { }

            string publishedManifest = Path.Combine(_outputRoot, "manifest.json");
            if (!File.Exists(publishedManifest))
            {
                throw new InvalidDataException("Published manifest missing after move.");
            }
        }
        catch (Exception)
        {
            try
            {
                // Rollback: delete any partially published output
                foreach (string entry in Directory.EnumerateFileSystemEntries(_outputRoot))
                {
                    string name = Path.GetFileName(entry);
                    if (name == ".staging" || name == ".replacement") continue;
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else if (File.Exists(entry))
                    {
                        File.Delete(entry);
                    }
                }

                // Restore backup
                foreach (string entry in Directory.EnumerateFileSystemEntries(backupDir))
                {
                    string dest = Path.Combine(_outputRoot, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        Directory.Move(entry, dest);
                    }
                    else if (File.Exists(entry))
                    {
                        File.Move(entry, dest);
                    }
                }
            }
            catch { }

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, recursive: true);
                }
            }
            catch { }

            throw;
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;

        const int bufferSize = 81920;
        using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        Span<byte> leftBuf = stackalloc byte[8192];
        Span<byte> rightBuf = stackalloc byte[8192];

        // Use heap buffers for larger
        byte[] leftHeap = new byte[8192];
        byte[] rightHeap = new byte[8192];

        int leftRead, rightRead;
        do
        {
            leftRead = leftStream.Read(leftHeap, 0, leftHeap.Length);
            rightRead = rightStream.Read(rightHeap, 0, rightHeap.Length);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) break;
            if (!leftHeap.AsSpan(0, leftRead).SequenceEqual(rightHeap.AsSpan(0, rightRead)))
            {
                return false;
            }
        } while (leftRead > 0);

        return true;
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch { }
    }
}

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

    public void Recover()
    {
        string replacementRoot = Path.Combine(_outputRoot, ".replacement");
        if (!Directory.Exists(replacementRoot)) return;

        foreach (string wrapper in Directory.EnumerateDirectories(replacementRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string journal = Path.Combine(wrapper, ".journal");
            if (!File.Exists(journal)) continue;

            string payload = Path.Combine(wrapper, "payload");
            string manifestBackup = Path.Combine(payload, "manifest.json");
            string currentManifest = Path.Combine(_outputRoot, "manifest.json");

            try
            {
                string journalContent = File.ReadAllText(journal);
                // If journal says "started" and we have backup manifest, and current manifest is missing or is new partial, restore backup
                if (journalContent.Contains("started") && File.Exists(manifestBackup))
                {
                    // If current manifest missing or its content differs from staging manifest that was being published, restore backup
                    // For safety, if current manifest does not exist or is not valid, restore
                    bool needRestore = !File.Exists(currentManifest);
                    if (!needRestore)
                    {
                        try
                        {
                            // Try to parse current manifest; if fails, restore
                            ContentManifest.LoadFromFile(currentManifest);
                        }
                        catch
                        {
                            needRestore = true;
                        }
                    }
                    if (needRestore)
                    {
                        File.Copy(manifestBackup, currentManifest, overwrite: true);
                    }
                }
            }
            catch { }

            try
            {
                if (File.Exists(journal)) File.Delete(journal);
                // After successful recovery, clean up the entire wrapper (including payload) to avoid garbage
                if (Directory.Exists(wrapper))
                {
                    try { Directory.Delete(wrapper, recursive: true); } catch { }
                }
            }
            catch { }
        }

        try
        {
            if (Directory.Exists(replacementRoot) && !Directory.EnumerateFileSystemEntries(replacementRoot).Any())
                Directory.Delete(replacementRoot);
        }
        catch { }
    }

    public void Publish(string stagingDirectory)
    {
        string stagingFull = Path.GetFullPath(stagingDirectory);
        if (!PathSafety.IsContained(_stagingRoot, stagingFull) && !string.Equals(stagingFull.TrimEnd(Path.DirectorySeparatorChar), _stagingRoot.TrimEnd(Path.DirectorySeparatorChar), PathSafety.PathComparison))
        {
            if (!PathSafety.IsContained(_stagingRoot, stagingFull))
                throw new ArgumentException($"Staging directory must be under {_stagingRoot}", nameof(stagingDirectory));
        }

        if (!Directory.Exists(stagingFull))
            throw new DirectoryNotFoundException($"Staging directory not found: {stagingFull}");

        Directory.CreateDirectory(_outputRoot);

        string stagingManifest = Path.Combine(stagingFull, "manifest.json");
        if (!File.Exists(stagingManifest))
            throw new InvalidDataException($"Staging manifest missing: {stagingManifest}");

        string replacementRoot = Path.Combine(_outputRoot, ".replacement");
        Directory.CreateDirectory(replacementRoot);
        string txId = Guid.NewGuid().ToString("N");
        string backupDir = Path.Combine(replacementRoot, txId);
        string payloadDir = Path.Combine(backupDir, "payload");
        Directory.CreateDirectory(payloadDir);
        string journalPath = Path.Combine(backupDir, ".journal");
        File.WriteAllText(journalPath, "started");

        // Backup current manifest if exists (for atomic manifest swap)
        string currentManifest = Path.Combine(_outputRoot, "manifest.json");
        string backupManifest = Path.Combine(payloadDir, "manifest.json");
        bool hadManifest = File.Exists(currentManifest);
        if (hadManifest)
        {
            File.Copy(currentManifest, backupManifest, overwrite: true);
        }

        try
        {
            // Move artifacts first (all files except manifest.json), with FilesEqual optimization to avoid rewrite
            // Collect backup map for reuse check (for artifacts that will be overwritten)
            var backupArtifactMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (hadManifest)
            {
                // Build map of existing artifacts for reuse check
                string currentArtifactsRoot = Path.Combine(_outputRoot, "artifacts");
                if (Directory.Exists(currentArtifactsRoot))
                {
                    foreach (string f in Directory.EnumerateFiles(currentArtifactsRoot, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(_outputRoot, f).Replace(Path.DirectorySeparatorChar, '/');
                        backupArtifactMap[rel] = f;
                    }
                }
            }

            var stagingFiles = Directory.EnumerateFiles(stagingFull, "*", SearchOption.AllDirectories).ToList();
            var manifestFile = stagingFiles.FirstOrDefault(f => string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.OrdinalIgnoreCase));
            var artifactFiles = stagingFiles.Where(f => !string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();

            // Move artifacts (excluding manifest) first
            foreach (string stagingFile in artifactFiles)
            {
                string rel = Path.GetRelativePath(stagingFull, stagingFile).Replace(Path.DirectorySeparatorChar, '/');
                string dest = Path.Combine(_outputRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string? destDir = Path.GetDirectoryName(dest);
                if (destDir is not null) Directory.CreateDirectory(destDir);

                // If backup has same relative path and content equal, reuse (no rewrite) — keep existing file, delete staging
                if (backupArtifactMap.TryGetValue(rel, out string? existing) && FilesEqual(existing, stagingFile))
                {
                    File.Delete(stagingFile);
                    backupArtifactMap.Remove(rel);
                }
                else
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    // If backup had this file but not equal, remove backup entry (it will be considered stale)
                    if (backupArtifactMap.ContainsKey(rel))
                    {
                        try { File.Delete(backupArtifactMap[rel]); } catch { }
                        backupArtifactMap.Remove(rel);
                    }
                    File.Move(stagingFile, dest);
                }
            }

            // Now atomically replace manifest last (only if not byte-identical)
            if (manifestFile is not null)
            {
                string destManifest = Path.Combine(_outputRoot, "manifest.json");
                bool manifestEqual = hadManifest && FilesEqual(manifestFile, destManifest);
                if (manifestEqual)
                {
                    // No rewrite needed — keep existing timestamp
                    File.Delete(manifestFile);
                }
                else if (hadManifest)
                {
                    // Use Replace for atomic overwrite (backs up old to backupManifest already, but Replace needs backup path)
                    string tmpBackup = destManifest + ".tmpbak";
                    try { if (File.Exists(tmpBackup)) File.Delete(tmpBackup); } catch { }
                    File.Replace(manifestFile, destManifest, tmpBackup);
                    try { if (File.Exists(tmpBackup)) File.Delete(tmpBackup); } catch { }
                }
                else
                {
                    File.Move(manifestFile, destManifest);
                }
            }

            // Delete remaining stale artifacts that were in backup but not in new staging (not overwritten)
            // backupArtifactMap now contains only stale files not in new staging (or those we kept)
            foreach (var kv in backupArtifactMap)
            {
                try
                {
                    string full = kv.Value;
                    if (File.Exists(full)) File.Delete(full);
                }
                catch { }
            }
            // Clean empty artifact directories in output
            TryDeleteEmptyDirectories(Path.Combine(_outputRoot, "artifacts"));
            TryDeleteEmptyDirectories(backupDir);

            // Cleanup staging directory (should be empty or contain only empty dirs)
            if (Directory.Exists(stagingFull))
            {
                try { Directory.Delete(stagingFull, recursive: true); } catch { }
            }

            // Delete backup payload and journal on success
            if (Directory.Exists(payloadDir))
            {
                try { Directory.Delete(payloadDir, recursive: true); } catch { }
            }
            if (Directory.Exists(backupDir))
            {
                try { Directory.Delete(backupDir, recursive: true); } catch { }
            }
            try { if (File.Exists(journalPath)) File.Delete(journalPath); } catch { }

            try
            {
                if (Directory.Exists(replacementRoot) && !Directory.EnumerateFileSystemEntries(replacementRoot).Any())
                    Directory.Delete(replacementRoot);
            }
            catch { }

            if (!File.Exists(Path.Combine(_outputRoot, "manifest.json")))
                throw new InvalidDataException("Published manifest missing after move.");
        }
        catch (Exception)
        {
            // Rollback manifest if we replaced it: restore from backup
            try
            {
                if (File.Exists(backupManifest))
                {
                    // If current manifest is missing or invalid, restore backup
                    string destManifest = Path.Combine(_outputRoot, "manifest.json");
                    try
                    {
                        if (!File.Exists(destManifest))
                            File.Copy(backupManifest, destManifest, overwrite: true);
                        else
                        {
                            // Try to validate current manifest; if invalid, restore
                            try { ContentManifest.LoadFromFile(destManifest); }
                            catch { File.Copy(backupManifest, destManifest, overwrite: true); }
                        }
                    }
                    catch { File.Copy(backupManifest, destManifest, overwrite: true); }
                }
            }
            catch { }

            try
            {
                if (Directory.Exists(backupDir))
                {
                    // Keep journal for next Recover
                    if (!File.Exists(journalPath))
                        File.WriteAllText(journalPath, "started");
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
        byte[] leftHeap = new byte[8192];
        byte[] rightHeap = new byte[8192];
        int leftRead, rightRead;
        do
        {
            leftRead = leftStream.Read(leftHeap, 0, leftHeap.Length);
            rightRead = rightStream.Read(rightHeap, 0, rightHeap.Length);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) break;
            if (!leftHeap.AsSpan(0, leftRead).SequenceEqual(rightHeap.AsSpan(0, rightRead))) return false;
        } while (leftRead > 0);
        return true;
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }
        catch { }
    }
}

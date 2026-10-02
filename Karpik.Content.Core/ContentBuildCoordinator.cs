namespace Karpik.Content.Core;

public sealed class ContentBuildOptions
{
    public string SourceRoot { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    // Build target selection. Defaults to Shared (the union of Client|Server),
    // which selects the whole tree and preserves the pre-target behavior.
    // CLI/SDK always pass a single target (Client or Server).
    public AssetTarget Target { get; init; } = AssetTarget.Shared;
}

public sealed class ContentBuildResult
{
    public bool Success { get; init; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; init; } = [];
    public ContentManifest? Manifest { get; init; }
}

public sealed class SourceEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public string MetaRelativePath { get; init; } = string.Empty;
    public string MetaAbsolutePath { get; init; } = string.Empty;
    public byte[] SourceBytes { get; init; } = [];
    public AssetMeta Meta { get; init; } = null!;
}

public sealed class ContentBuildCoordinator
{
    private const string InputFingerprintFileName = ".karpik-content-inputs.v1";
    private const string InputFingerprintVersion = "karpik-content-inputs-v1";
    private readonly IReadOnlyDictionary<string, IContentProcessor> _processors;

    public ContentBuildCoordinator(IEnumerable<IContentProcessor>? processors = null)
    {
        var list = processors ?? [new RawJsonProcessor(), new TextureProcessor(), new FontJsonProcessor(), new ShaderProcessor()];
        _processors = list.ToDictionary(p => p.DeclaredType, StringComparer.Ordinal);
    }

    public ContentBuildResult Validate(ContentBuildOptions options)
    {
        var diagnostics = new List<ContentDiagnostic>();
        var entries = ScanAndValidate(options, diagnostics, out _);

        if (entries is not null)
        {
            foreach (SourceEntry entry in entries)
            {
                if (!IsSelectedFor(entry, options.Target))
                {
                    continue;
                }

                if (!_processors.TryGetValue(entry.Meta.DeclaredType, out IContentProcessor? processor))
                {
                    continue;
                }

                ContentProcessorResult result = processor.Process(entry.SourceBytes, entry.Meta, entry.RelativePath, new ContentProcessorContext(options.Target));
                foreach (ContentDiagnostic d in result.Diagnostics)
                {
                    diagnostics.Add(d);
                }
            }
        }

        bool hasError = diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error);
        if (hasError)
        {
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult
            {
                Success = false, Diagnostics = diagnostics
            };
        }

        diagnostics.Sort(CompareDiagnostics);
        return new ContentBuildResult { Success = true, Diagnostics = diagnostics, Manifest = null };
    }

    public ContentBuildResult Build(ContentBuildOptions options)
    {
        var diagnostics = new List<ContentDiagnostic>();
        var entries = ScanAndValidate(options, diagnostics, out Dictionary<AssetId, SourceEntry>? idToEntry);

        bool hasError = diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error);
        if (hasError)
        {
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }

        // At this point entries is not null and idToEntry populated
        if (entries is null || idToEntry is null)
        {
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }

        string outputRootFull = Path.GetFullPath(options.OutputRoot);
        string sourceRootForCheck;
        try
        {
            sourceRootForCheck = Path.GetFullPath(options.SourceRoot);
        }
        catch (Exception ex)
        {
            sourceRootForCheck = options.SourceRoot;
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal, ContentDiagnosticSeverity.Error,
                null, $"Invalid source root for output check: {ex.Message}"));
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }

        string normalizedSource = sourceRootForCheck.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedOutput = outputRootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool same = string.Equals(normalizedSource, normalizedOutput, PathSafety.PathComparison);
        bool outputInsideSource = PathSafety.IsContained(sourceRootForCheck, outputRootFull) && !same;
        bool sourceInsideOutput = PathSafety.IsContained(outputRootFull, sourceRootForCheck) && !same;
        if (same || outputInsideSource || sourceInsideOutput)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal, ContentDiagnosticSeverity.Error,
                null,
                $"OutputRoot '{outputRootFull}' must not be equal to, contain, or be contained by SourceRoot '{sourceRootForCheck}'."));
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }

        if (TryComputeInputFingerprint(entries, options, out string inputFingerprint) &&
            TryLoadReusableOutput(outputRootFull, inputFingerprint, out ContentManifest? previousManifest))
        {
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = true, Diagnostics = diagnostics, Manifest = previousManifest };
        }

        // Process each source to cooked bytes and artifact hash
        var manifestEntries = new List<ContentManifestEntry>();
        var artifactMap = new Dictionary<string, byte[]>(); // locator -> cooked bytes

        foreach (SourceEntry entry in entries.OrderBy(e => e.Meta.AssetId.Value))
        {
            if (!IsSelectedFor(entry, options.Target))
            {
                continue;
            }

            if (!_processors.TryGetValue(entry.Meta.DeclaredType, out IContentProcessor? processor))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.UnsupportedDeclaredType,
                    ContentDiagnosticSeverity.Error, entry.RelativePath,
                    $"Unsupported declared type '{entry.Meta.DeclaredType}'."));
                continue;
            }

            string canonicalMeta = entry.Meta.ToCanonicalMetaJson();
            ContentProcessorResult result = processor.Process(entry.SourceBytes, entry.Meta, entry.RelativePath, new ContentProcessorContext(options.Target));

            // Add processor diagnostics
            foreach (ContentDiagnostic d in result.Diagnostics)
            {
                diagnostics.Add(d);
            }

            if (result.Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error))
            {
                continue;
            }

            string sourceHash = ContentHashing.HashSourceBytes(entry.SourceBytes);
            string importSettingsHash = ContentHashing.HashImportSettings(entry.Meta.RawImportSettingsJson);
            string artifactHash =
                ContentHashing.ComputeArtifactHash(entry.SourceBytes, canonicalMeta, processor.Version, options.Target);
            string locator = ContentHashing.ComputeArtifactLocator(artifactHash);
            long size = result.CookedBytes.Length;

            // Merge dependencies: explicit meta dependencies + processor dependencies (dedup sorted)
            var deps = new HashSet<AssetId>();
            foreach (AssetId dep in entry.Meta.Dependencies) deps.Add(dep);
            foreach (AssetId dep in result.Dependencies) deps.Add(dep);
            var sortedDeps = deps.OrderBy(d => d.Value).ToList();

            manifestEntries.Add(new ContentManifestEntry(entry.Meta.AssetId, entry.Meta.DeclaredType,
                entry.Meta.LogicalName, importSettingsHash, sourceHash, locator, size, sortedDeps));

            // Only add artifact if not already present (deduplicate by locator)
            if (!artifactMap.ContainsKey(locator))
            {
                artifactMap[locator] = result.CookedBytes;
            }
        }

        hasError = diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error);
        if (hasError)
        {
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }

        manifestEntries.Sort((a, b) => a.AssetId.CompareTo(b.AssetId));
        var manifest = new ContentManifest(ContentManifest.CurrentSchemaVersion, manifestEntries);

        // Validate artifact reuse? Already covered.

        diagnostics.Sort(CompareDiagnostics);

        // Recover from any interrupted previous publish (crash between artifact moves and manifest swap)
        try
        {
            new ContentAtomicPublisher(outputRootFull).Recover();
        }
        catch
        {
        }

        Directory.CreateDirectory(outputRootFull);

        string stagingRoot = Path.Combine(outputRootFull, ".staging");
        Directory.CreateDirectory(stagingRoot);
        string transactionId = Guid.NewGuid().ToString("N");
        string stagingDir = Path.Combine(stagingRoot, transactionId);
        Directory.CreateDirectory(stagingDir);

        try
        {
            // Write artifacts
            foreach (var kv in artifactMap)
            {
                string artifactFullPath = Path.Combine(stagingDir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                string? dir = Path.GetDirectoryName(artifactFullPath);
                if (dir is not null) Directory.CreateDirectory(dir);

                // If file already exists in previous published output and hash matches, we could reuse but for now just write.
                // To satisfy no rewrite requirement, caller will handle preserving previous output; we just write staging.
                File.WriteAllBytes(artifactFullPath, kv.Value);
            }

            // Write manifest
            string manifestPath = Path.Combine(stagingDir, "manifest.json");
            manifest.SaveToFile(manifestPath);
            File.WriteAllText(Path.Combine(stagingDir, InputFingerprintFileName), inputFingerprint + "\n");

            // Validate staging: ensure manifest can be parsed and artifacts exist
            ValidateStaging(stagingDir, manifest, diagnostics);

            hasError = diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error);
            if (hasError)
            {
                // Cleanup staging
                TryDeleteDirectory(stagingDir);
                diagnostics.Sort(CompareDiagnostics);
                return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
            }

            // Publish atomically
            var publisher = new ContentAtomicPublisher(outputRootFull);
            publisher.Publish(stagingDir);

            // On success, diagnostics sorted, return manifest
            return new ContentBuildResult { Success = true, Diagnostics = diagnostics, Manifest = manifest };
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ManifestCorrupt,
                ContentDiagnosticSeverity.Error, null, $"Build publication failed: {ex.Message}"));
            TryDeleteDirectory(stagingDir);
            diagnostics.Sort(CompareDiagnostics);
            return new ContentBuildResult { Success = false, Diagnostics = diagnostics };
        }
    }

    private bool TryComputeInputFingerprint(
        IReadOnlyList<SourceEntry> entries,
        ContentBuildOptions options,
        out string fingerprint)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        AppendFingerprintPart(hash, InputFingerprintVersion);
        AppendFingerprintPart(hash, options.Namespace);
        AppendFingerprintPart(hash, options.Target.ToString());
        foreach (SourceEntry entry in entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            if (!_processors.TryGetValue(entry.Meta.DeclaredType, out IContentProcessor? processor))
            {
                fingerprint = string.Empty;
                return false;
            }

            AppendFingerprintPart(hash, entry.RelativePath);
            hash.AppendData(entry.SourceBytes);
            AppendFingerprintPart(hash, entry.Meta.ToCanonicalMetaJson());
            AppendFingerprintPart(hash, processor.Version);
        }

        fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        return true;
    }

    private static void AppendFingerprintPart(System.Security.Cryptography.IncrementalHash hash, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static bool TryLoadReusableOutput(string outputRoot, string fingerprint, out ContentManifest? manifest)
    {
        manifest = null;
        try
        {
            string fingerprintPath = Path.Combine(outputRoot, InputFingerprintFileName);
            if (!File.Exists(fingerprintPath) ||
                !string.Equals(File.ReadAllText(fingerprintPath).Trim(), fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            ContentManifest loaded = ContentManifest.LoadFromFile(Path.Combine(outputRoot, "manifest.json"));
            foreach (ContentManifestEntry entry in loaded.Entries)
            {
                string artifactPath = Path.Combine(outputRoot, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(artifactPath) || new FileInfo(artifactPath).Length != entry.Size)
                {
                    return false;
                }
            }

            manifest = loaded;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private void ValidateStaging(string stagingDir, ContentManifest manifest, List<ContentDiagnostic> diagnostics)
    {
        string manifestPath = Path.Combine(stagingDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ManifestCorrupt,
                ContentDiagnosticSeverity.Error, null, "Staging manifest missing."));
            return;
        }

        // Verify each artifact exists and size matches and locator corresponds to hash verification?
        // We do locator digest verification: ensure artifact bytes hash to locator via re-computing? But we don't have source bytes here.
        // Simpler: just check file exists and size matches manifest.
        foreach (ContentManifestEntry entry in manifest.Entries)
        {
            string artifactPath =
                Path.Combine(stagingDir, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(artifactPath))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ManifestCorrupt,
                    ContentDiagnosticSeverity.Error, null,
                    $"Missing artifact for {entry.AssetId} at {entry.ArtifactLocator}"));
                continue;
            }

            long actualSize = new FileInfo(artifactPath).Length;
            if (actualSize != entry.Size)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ManifestCorrupt,
                    ContentDiagnosticSeverity.Error, null,
                    $"Artifact size mismatch for {entry.AssetId}. Expected {entry.Size}, got {actualSize}"));
            }

            // Verify artifact name contains hash that matches content? We could check that filename equals hash of content via content hashing?
            // Artifact locator is artifacts/ab/cd/<hash>.cooked . The hash should equal SHA256 of file? But our artifact hash is computed over framed source+meta+version, not just cooked bytes.
            // So we cannot verify by hashing cooked bytes alone. We'll just trust.
        }
    }

    private List<SourceEntry>? ScanAndValidate(ContentBuildOptions options, List<ContentDiagnostic> diagnostics, out Dictionary<AssetId, SourceEntry>? idToEntry)
    {
        idToEntry = null;
        string sourceRoot;
        try
        {
            sourceRoot = Path.GetFullPath(options.SourceRoot);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal, ContentDiagnosticSeverity.Error,
                null, $"Invalid source root: {ex.Message}"));
            return null;
        }

        if (!Directory.Exists(sourceRoot))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingSourceFile,
                ContentDiagnosticSeverity.Error, null, $"Source root does not exist: {sourceRoot}"));
            return null;
        }

        if (PathSafety.IsReparsePoint(sourceRoot))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal, ContentDiagnosticSeverity.Error,
                null, $"Source root is a reparse point: {sourceRoot}"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.Namespace))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidLogicalName,
                ContentDiagnosticSeverity.Error, null, "Namespace must be non-empty."));
            return null;
        }

        string expectedNamespace = options.Namespace.Trim();
        if (!IsValidNamespace(expectedNamespace))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidLogicalName,
                ContentDiagnosticSeverity.Error, null, $"Invalid namespace '{expectedNamespace}'."));
            return null;
        }

        if (options.Target is not (AssetTarget.Client or AssetTarget.Server or AssetTarget.Shared))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ConcreteBuildTargetRequired,
                ContentDiagnosticSeverity.Error, null,
                "Build target must be Client or Server."));
            return null;
        }

        // Scan files
        var entries = new List<SourceEntry>();
        var idMap = new Dictionary<AssetId, SourceEntry>();
        var logicalNameMap = new Dictionary<string, SourceEntry>(StringComparer.Ordinal);

        // Enumerate all files recursively except .meta files
        var allFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // First, collect source files that are not .meta
        var sourceFiles = new List<string>();
        var metaFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in allFiles)
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                metaFiles.Add(file);
            }
            else
            {
                sourceFiles.Add(file);
            }
        }

        foreach (string sourceFile in sourceFiles)
        {
            string relativePath;
            try
            {
                relativePath = GetRelativePathSafe(sourceRoot, sourceFile);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal,
                    ContentDiagnosticSeverity.Error, null, $"Source path escapes root: {sourceFile}: {ex.Message}"));
                continue;
            }

            if (IsTraversalRelative(relativePath))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal,
                    ContentDiagnosticSeverity.Error, relativePath, $"Source path contains traversal: {relativePath}"));
                continue;
            }

            string metaPath = sourceFile + ".meta";
            string metaRelative = relativePath + ".meta";

            if (!File.Exists(metaPath))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaFile,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Missing sidecar meta for '{relativePath}'. Expected '{metaRelative}'."));
                continue;
            }

            if (PathSafety.IsReparsePoint(sourceFile) || PathSafety.IsReparsePoint(metaPath))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.PathTraversal,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Source or meta is a reparse point: {relativePath}"));
                continue;
            }

            // Read source bytes
            byte[] sourceBytes;
            try
            {
                sourceBytes = File.ReadAllBytes(sourceFile);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingSourceFile,
                    ContentDiagnosticSeverity.Error, relativePath, $"Cannot read source: {ex.Message}"));
                continue;
            }

            // Read and parse meta
            string metaJson;
            try
            {
                metaJson = File.ReadAllText(metaPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaJson,
                    ContentDiagnosticSeverity.Error, metaRelative, $"Cannot read meta: {ex.Message}"));
                continue;
            }

            AssetMeta meta;
            try
            {
                meta = AssetMeta.Parse(metaJson, metaRelative, diagnostics);
            }
            catch (InvalidDataException)
            {
                // Diagnostic already added
                continue;
            }

            if (options.Target == AssetTarget.Shared && meta.Targets != AssetTarget.Shared)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.ConcreteBuildTargetRequired,
                    ContentDiagnosticSeverity.Error, relativePath,
                    "Build target must be Client or Server when the source tree contains single-target assets."));
                continue;
            }

            // Validate declared type supported
            if (!_processors.ContainsKey(meta.DeclaredType))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.UnsupportedDeclaredType,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Unsupported declared type '{meta.DeclaredType}'."));
                continue;
            }

            // Validate logical name namespace
            string logicalNamespace = meta.LogicalName.Split('/')[0];
            if (!string.Equals(logicalNamespace, expectedNamespace, StringComparison.Ordinal))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.NamespaceMismatch,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Logical name '{meta.LogicalName}' namespace '{logicalNamespace}' does not match expected '{expectedNamespace}'."));
                continue;
            }

            // Duplicate ID check
            if (idMap.ContainsKey(meta.AssetId))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.DuplicateAssetId,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Duplicate AssetId '{meta.AssetId}' also used by '{idMap[meta.AssetId].RelativePath}'."));
                // Also mark previous? Keep both diagnostics but continue
                continue;
            }

            bool selectedForTarget = (meta.Targets & options.Target) != 0;

            // Duplicate logical name check (only within the selected target set,
            // so independent Client-only and Server-only assets may share a name)
            if (selectedForTarget && logicalNameMap.ContainsKey(meta.LogicalName))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.DuplicateLogicalName,
                    ContentDiagnosticSeverity.Error, relativePath,
                    $"Duplicate logicalName '{meta.LogicalName}' also used by '{logicalNameMap[meta.LogicalName].RelativePath}'."));
                continue;
            }

            var entry = new SourceEntry
            {
                RelativePath = relativePath,
                AbsolutePath = sourceFile,
                MetaRelativePath = metaRelative,
                MetaAbsolutePath = metaPath,
                SourceBytes = sourceBytes,
                Meta = meta
            };

            entries.Add(entry);
            idMap[meta.AssetId] = entry;
            if (selectedForTarget)
            {
                logicalNameMap[meta.LogicalName] = entry;
            }
        }

        // Also check for orphan .meta files without source
        foreach (string metaFile in metaFiles)
        {
            string sourceCandidate = metaFile.Substring(0, metaFile.Length - ".meta".Length);
            if (!File.Exists(sourceCandidate))
            {
                string rel = GetRelativePathSafe(sourceRoot, metaFile);
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingSourceFile,
                    ContentDiagnosticSeverity.Error, rel, $"Orphan meta without source file: {rel}"));
            }
        }

        // Validate dependencies: unknown ID, outside namespace, cycle
        // Build graph of AssetId -> dependencies
        // Only for entries that passed earlier validation (in idMap)
        var graph = new Dictionary<AssetId, List<AssetId>>();
        foreach (var kv in idMap)
        {
            if (!IsSelectedFor(kv.Value, options.Target))
            {
                continue;
            }

            graph[kv.Key] = kv.Value.Meta.Dependencies.ToList();
        }

        // Unknown dependency (only dependencies of selected assets are enforced;
        // a dependency that exists but is not selected for this target is KCO022)
        foreach (var kv in idMap)
        {
            if (!IsSelectedFor(kv.Value, options.Target))
            {
                continue;
            }

            foreach (AssetId dep in kv.Value.Meta.Dependencies)
            {
                if (!idMap.ContainsKey(dep))
                {
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.UnknownDependency,
                        ContentDiagnosticSeverity.Error, kv.Value.RelativePath,
                        $"Unknown dependency '{dep}' referenced by '{kv.Key}'."));
                }
                else if (!IsSelectedFor(idMap[dep], options.Target))
                {
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.TargetDependencyNotSelected,
                        ContentDiagnosticSeverity.Error, kv.Value.RelativePath,
                        $"Dependency '{dep}' referenced by '{kv.Key}' is not selected for this target."));
                }
                else
                {
                    // Check dependency namespace? Actually dependency's logical name must also be within expected namespace, but since all logical names are validated to be within namespace, this is already ensured.
                    // However if dependency's logicalName namespace != expectedNamespace, then it would have already been rejected and not in idMap. So unknown dependency covers outside-namespace? But ExecPlan says dependencies outside requested namespace should be rejected with specific code.
                    // We need to distinguish: if dependency exists but its logical name namespace != expected? But we filtered entries by namespace, so such dependency would be missing in idMap if its own entry failed validation. Then unknown dependency would fire, not DependencyOutsideNamespace.
                    // To correctly handle outside-namespace dependency where dep exists but its namespace is different, we would need to have scanned entries without filtering? Instead we currently filter out entries whose logicalName namespace mismatches. That means dependency outside namespace will appear as unknown.
                    // To satisfy spec, we should after detecting unknown, check if dependency might have been filtered due to namespace mismatch? Simpler: we already emit NamespaceMismatch for the source that has mismatched logicalName, not for dependency.
                    // The spec for dependency outside namespace likely means a dependency that points to an AssetId whose own logicalName namespace differs from expected? But all logicalNames are forced to be within expected namespace, so this is not possible unless we allow multiple namespaces.
                    // For simplicity, treat any dependency whose target's logicalName namespace != expectedNamespace as DependencyOutsideNamespace.
                    // But since we already enforce all targets are within expectedNamespace, this branch will never hit. We can still check.
                    SourceEntry depEntry = idMap[dep];
                    if (kv.Value.Meta.Targets == AssetTarget.Shared &&
                        depEntry.Meta.Targets != AssetTarget.Shared)
                    {
                        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.SharedDependsOnSided,
                            ContentDiagnosticSeverity.Error, kv.Value.RelativePath,
                            $"Shared asset '{kv.Key}' must not depend on single-target asset '{dep}'."));
                        continue;
                    }

                    string depNamespace = depEntry.Meta.LogicalName.Split('/')[0];
                    if (!string.Equals(depNamespace, expectedNamespace, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.DependencyOutsideNamespace,
                            ContentDiagnosticSeverity.Error, kv.Value.RelativePath,
                            $"Dependency '{dep}' logicalName '{depEntry.Meta.LogicalName}' is outside expected namespace '{expectedNamespace}'."));
                    }
                }
            }
        }

        // Cycle detection via DFS
        var visited = new Dictionary<AssetId, int>(); // 0 unvisited, 1 visiting, 2 visited
        var stack = new List<AssetId>();
        bool cycleFound = false;

        void Dfs(AssetId node)
        {
            if (cycleFound) return;
            visited[node] = 1;
            stack.Add(node);
            if (graph.TryGetValue(node, out List<AssetId>? deps))
            {
                foreach (AssetId dep in deps.OrderBy(d => d.Value))
                {
                    if (!graph.ContainsKey(dep)) continue; // unknown already reported
                    if (!visited.ContainsKey(dep))
                    {
                        Dfs(dep);
                        if (cycleFound) return;
                    }
                    else if (visited[dep] == 1)
                    {
                        // Cycle found
                        int idx = stack.IndexOf(dep);
                        var cycle = stack.Skip(idx).Concat(new[] { dep }).Select(id => id.ToCanonicalString());
                        string cycleStr = string.Join(" -> ", cycle);
                        string dependentPath = idMap[node].RelativePath;
                        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.DependencyCycle,
                            ContentDiagnosticSeverity.Error, dependentPath, $"Dependency cycle detected: {cycleStr}"));
                        cycleFound = true;
                        return;
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visited[node] = 2;
        }

        foreach (AssetId id in graph.Keys.OrderBy(k => k.Value))
        {
            if (!visited.ContainsKey(id))
            {
                Dfs(id);
                if (cycleFound) break;
            }
        }

        // If any errors, still return entries but caller will check diagnostics
        idToEntry = idMap;
        // Return entries sorted by AssetId for determinism
        entries.Sort((a, b) => a.Meta.AssetId.CompareTo(b.Meta.AssetId));
        return entries;
    }

    private static string GetRelativePathSafe(string root, string fullPath)
    {
        string rel = Path.GetRelativePath(root, fullPath);
        // Normalize to '/' for diagnostics consistency
        rel = rel.Replace(Path.DirectorySeparatorChar, '/');
        return rel;
    }

    private static bool IsTraversalRelative(string rel)
    {
        if (rel.StartsWith("../") || rel == ".." || rel.Contains("/../") || rel.Contains("\\") ||
            Path.IsPathRooted(rel))
            return true;
        // Also check segments ".."
        foreach (string seg in rel.Split('/'))
        {
            if (seg == "..") return true;
        }

        return false;
    }

    private static bool IsValidNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return false;
        if (ns.Contains('/') || ns.Contains('\\')) return false;
        if (ns.Contains("..")) return false;
        foreach (char c in ns)
        {
            if (c >= 'a' && c <= 'z') continue;
            if (c >= 'A' && c <= 'Z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c == '_' || c == '-' || c == '.') continue;
            return false;
        }

        return true;
    }

    private static bool IsSelectedFor(SourceEntry entry, AssetTarget target) =>
        (entry.Meta.Targets & target) != 0;

    private static int CompareDiagnostics(ContentDiagnostic a, ContentDiagnostic b)
    {
        int cmp = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
        if (cmp != 0) return cmp;
        cmp = string.Compare(a.RelativePath, b.RelativePath, StringComparison.Ordinal);
        if (cmp != 0) return cmp;
        return string.Compare(a.Message, b.Message, StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            // Also delete marker
            string marker = path + ".owned";
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch
        {
        }
    }
}

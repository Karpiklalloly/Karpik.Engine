using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Karpik.Engine.Tooling;

namespace Karpik.Engine.Packager;

public static class PayloadLayout
{
    public const string EditorDirectory = "editor";
    public const string SdkDirectory = "sdk";
    public const string ClientRunnerDirectory = "runners/client";
    public const string ServerRunnerDirectory = "runners/server";
    public const string ModulesDirectory = "modules";
    public const string NativeDirectory = "native";
    public const string ManifestFileName = "engine-installation.json";
    public const string CompletionMarkerFileName = ".complete";
    public const string RunnerAssemblyFileName = "Karpik.Engine.Core.Runner.dll";

    internal static void Materialize(
        string sourceRoot,
        string stagingRoot,
        string sdkVersion,
        DotNetProcessRunner processRunner)
    {
        if (IsPreparedPayload(sourceRoot))
        {
            CopyPreparedPayload(sourceRoot, stagingRoot);
            return;
        }
        if (!File.Exists(Path.Combine(sourceRoot, "KarpikEngine.slnx")))
        {
            throw new InvalidDataException(
                "--source must be either a prepared payload with editor/sdk/runners/modules/native directories or a KarpikEngine source root.");
        }

        MaterializeRepository(sourceRoot, stagingRoot, sdkVersion, processRunner);
    }

    private static bool IsPreparedPayload(string root) =>
        Directory.Exists(Path.Combine(root, EditorDirectory)) &&
        Directory.Exists(Path.Combine(root, SdkDirectory)) &&
        Directory.Exists(Path.Combine(root, "runners", "client")) &&
        Directory.Exists(Path.Combine(root, "runners", "server")) &&
        Directory.Exists(Path.Combine(root, ModulesDirectory)) &&
        Directory.Exists(Path.Combine(root, NativeDirectory));

    private static void CopyPreparedPayload(string sourceRoot, string stagingRoot)
    {
        foreach (string relativeDirectory in new[]
                 {
                     EditorDirectory,
                     SdkDirectory,
                     ClientRunnerDirectory,
                     ServerRunnerDirectory,
                     ModulesDirectory,
                     NativeDirectory
                 })
        {
            string relative = relativeDirectory.Replace('/', Path.DirectorySeparatorChar);
            CopyDirectory(Path.Combine(sourceRoot, relative), Path.Combine(stagingRoot, relative));
        }
    }

    private static void MaterializeRepository(
        string repositoryRoot,
        string stagingRoot,
        string sdkVersion,
        DotNetProcessRunner processRunner)
    {
        string scratch = Path.Combine(stagingRoot, ".build");
        string artifacts = Path.Combine(scratch, "artifacts");
        string sdkOutput = Path.Combine(scratch, "sdk");
        Directory.CreateDirectory(sdkOutput);
        string artifactsProperty = $"-p:ArtifactsPath={artifacts}";
        string pathMapProperty = $"-p:PathMap={scratch}=/_karpik_owned_build";
        string[] deterministicProperties =
        [
            "-p:UseArtifactsOutput=true",
            artifactsProperty,
            "-p:ContinuousIntegrationBuild=true",
            "-p:Deterministic=true",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            pathMapProperty
        ];
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "restore", "KarpikEngine.slnx", "-m:1", "-nr:false", "-p:Configuration=Release");
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "build", "Karpik.Editor/Karpik.Editor.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false");
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "build", "Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false");
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "build", "Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false");
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "build", "Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/Karpik.Engine.Core.Codegen.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false");

        IReadOnlyList<SelectedModuleProject> moduleProjects = ReadSelectedModuleProjects(repositoryRoot);
        foreach (SelectedModuleProject moduleProject in moduleProjects)
        {
            RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "build", moduleProject.ProjectPath, "-c", "Release", "--no-restore", "-m:1", "-nr:false");
        }
        string sdkTasksOutput = GetArtifactOutput(artifacts, "Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj") + Path.DirectorySeparatorChar;
        string coreCodegenOutput = GetArtifactOutput(artifacts, "Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/Karpik.Engine.Core.Codegen.csproj") + Path.DirectorySeparatorChar;
        RunOwnedDotNet(processRunner, repositoryRoot, deterministicProperties, "pack", "Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj", "-c", "Release", "--no-restore", "-m:1", "-nr:false", $"-p:PackageVersion={sdkVersion}", $"-p:KarpikSdkTasksOutputPath={sdkTasksOutput}", $"-p:KarpikCoreCodegenOutputPath={coreCodegenOutput}", "-o", sdkOutput);
        CanonicalizeNuGetPackages(sdkOutput);

        string editorOutput = GetArtifactOutput(artifacts, "Karpik.Editor/Karpik.Editor.csproj");
        string runnerOutput = GetArtifactOutput(artifacts, "Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj");
        CopyDirectory(editorOutput, Path.Combine(stagingRoot, EditorDirectory));
        CopyDirectory(runnerOutput, Path.Combine(stagingRoot, "runners", "client"));
        CopyDirectory(runnerOutput, Path.Combine(stagingRoot, "runners", "server"));
        CopyDirectory(sdkOutput, Path.Combine(stagingRoot, SdkDirectory));

        string modulesDestination = Path.Combine(stagingRoot, ModulesDirectory);
        Directory.CreateDirectory(modulesDestination);
        foreach (SelectedModuleProject moduleProject in moduleProjects)
        {
            string moduleDestination = Path.Combine(modulesDestination, moduleProject.ModuleId);
            CopyDirectory(GetArtifactOutput(artifacts, moduleProject.ProjectPath), moduleDestination);
        }
        File.WriteAllText(
            Path.Combine(modulesDestination, EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize(moduleProjects.Select(project =>
                new EngineModuleCatalogEntry(project.ModuleId, project.Side))),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        string nativeDestination = Path.Combine(stagingRoot, NativeDirectory);
        Directory.CreateDirectory(nativeDestination);
        CopyNativeRuntimeFiles(runnerOutput, nativeDestination);
        foreach (SelectedModuleProject moduleProject in moduleProjects)
        {
            CopyNativeRuntimeFiles(GetArtifactOutput(artifacts, moduleProject.ProjectPath), nativeDestination);
        }
        string trackedNative = Path.Combine(repositoryRoot, "Modules", "Client", "Graphics", "Graphics.Core", "libveldrid-spirv.dll");
        if (File.Exists(trackedNative))
        {
            CopyFileMerged(trackedNative, Path.Combine(nativeDestination, Path.GetFileName(trackedNative)));
        }

        Directory.Delete(scratch, recursive: true);
    }

    private static void CanonicalizeNuGetPackages(string sdkOutput)
    {
        foreach (string packagePath in Directory.EnumerateFiles(sdkOutput, "*.nupkg", SearchOption.TopDirectoryOnly))
        {
            string canonicalPath = packagePath + ".canonical";
            using (ZipArchive source = ZipFile.OpenRead(packagePath))
            using (ZipArchive destination = ZipFile.Open(canonicalPath, ZipArchiveMode.Create))
            {
                string? corePropertiesName = source.Entries
                    .Select(entry => entry.FullName)
                    .SingleOrDefault(name => name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal));
                const string canonicalCorePropertiesName = "package/services/metadata/core-properties/core-properties.psmdcp";

                foreach (ZipArchiveEntry sourceEntry in source.Entries.OrderBy(entry => CanonicalEntryName(entry.FullName), StringComparer.Ordinal))
                {
                    string destinationName = sourceEntry.FullName == corePropertiesName
                        ? canonicalCorePropertiesName
                        : sourceEntry.FullName;
                    ZipArchiveEntry destinationEntry = destination.CreateEntry(destinationName, CompressionLevel.Optimal);
                    destinationEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using Stream destinationStream = destinationEntry.Open();
                    if (sourceEntry.FullName == "_rels/.rels" && corePropertiesName is not null)
                    {
                        using Stream sourceStream = sourceEntry.Open();
                        XDocument relationships = XDocument.Load(sourceStream, LoadOptions.PreserveWhitespace);
                        XElement[] relationshipElements = relationships.Descendants()
                            .Where(element => element.Name.LocalName == "Relationship")
                            .OrderBy(element => (string?)element.Attribute("Target"), StringComparer.Ordinal)
                            .ThenBy(element => (string?)element.Attribute("Type"), StringComparer.Ordinal)
                            .ToArray();
                        for (int index = 0; index < relationshipElements.Length; index++)
                        {
                            XElement relationship = relationshipElements[index];
                            XAttribute? target = relationship.Attribute("Target");
                            if (target is not null && target.Value.TrimStart('/') == corePropertiesName)
                            {
                                target.Value = "/" + canonicalCorePropertiesName;
                            }
                            relationship.SetAttributeValue("Id", $"R{index + 1}");
                        }
                        using var writer = new StreamWriter(destinationStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
                        relationships.Save(writer, SaveOptions.DisableFormatting);
                    }
                    else
                    {
                        using Stream sourceStream = sourceEntry.Open();
                        sourceStream.CopyTo(destinationStream);
                    }
                }
            }
            File.Move(canonicalPath, packagePath, overwrite: true);
        }

        static string CanonicalEntryName(string name) =>
            name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
                ? "package/services/metadata/core-properties/core-properties.psmdcp"
                : name;
    }

    private static IReadOnlyList<SelectedModuleProject> ReadSelectedModuleProjects(string repositoryRoot)
    {
        string targetsPath = Path.Combine(repositoryRoot, "AutoGenerated.targets");
        if (!File.Exists(targetsPath))
        {
            throw new FileNotFoundException("Repository source is missing AutoGenerated.targets.", targetsPath);
        }
        XDocument targets = XDocument.Load(targetsPath, LoadOptions.PreserveWhitespace);
        string modulesRoot = Path.Combine(repositoryRoot, "Modules");
        var projectPaths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (XElement reference in targets.Descendants().Where(element => element.Name.LocalName == "PluginReference"))
        {
            string? include = (string?)reference.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }
            string expanded = include.Replace("$(MSBuildThisFileDirectory)", repositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            string fullPath = Path.GetFullPath(expanded);
            if (!IsContained(modulesRoot, fullPath) || !File.Exists(fullPath))
            {
                continue;
            }
            projectPaths.Add(Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/'));
        }
        if (projectPaths.Count == 0)
        {
            throw new InvalidDataException("AutoGenerated.targets contains no selected first-party module projects below Modules/.");
        }

        var moduleIds = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
        var projects = new List<SelectedModuleProject>(projectPaths.Count);
        foreach (string projectPath in projectPaths)
        {
            string moduleId = Path.GetFileNameWithoutExtension(projectPath);
            if (!ModuleLayoutPolicy.IsSafeModuleId(moduleId))
            {
                throw new InvalidDataException($"Selected module project has an invalid module id '{moduleId}': {projectPath}");
            }
            if (!moduleIds.Add(moduleId))
            {
                throw new InvalidDataException($"Selected module id '{moduleId}' is not unique for this platform.");
            }
            projects.Add(new SelectedModuleProject(projectPath, moduleId, GetModuleSide(projectPath)));
        }
        return projects;
    }

    private static EngineModuleSide GetModuleSide(string projectPath)
    {
        string[] segments = projectPath.Split('/');
        if (segments.Length < 3 || !string.Equals(segments[0], "Modules", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Selected module is outside the canonical Modules/<side>/ layout: {projectPath}");
        }
        return segments[1] switch
        {
            "Shared" => EngineModuleSide.Shared,
            "Client" => EngineModuleSide.Client,
            "Server" => EngineModuleSide.Server,
            _ => throw new InvalidDataException($"Selected module has an unsupported runtime side: {projectPath}")
        };
    }

    private static string GetArtifactOutput(string artifactsRoot, string projectPath)
    {
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        return Path.Combine(artifactsRoot, "bin", projectName, "release");
    }

    private static bool IsContained(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void CopyNativeRuntimeFiles(string sourceRoot, string destinationRoot)
    {
        foreach (string file in EnumerateFilesSafe(sourceRoot))
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, file));
            if (!relative.Contains("/native/", StringComparison.OrdinalIgnoreCase) &&
                !relative.StartsWith("native/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string stripped = relative.StartsWith("native/", StringComparison.OrdinalIgnoreCase)
                ? relative["native/".Length..]
                : relative;
            CopyFileMerged(file, Path.Combine(destinationRoot, stripped.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static void RunOwnedDotNet(
        DotNetProcessRunner processRunner,
        string workingDirectory,
        IReadOnlyList<string> properties,
        params string[] arguments)
    {
        var combined = new string[arguments.Length + properties.Count];
        arguments.CopyTo(combined, 0);
        for (int index = 0; index < properties.Count; index++)
        {
            combined[arguments.Length + index] = properties[index];
        }
        processRunner.Run(workingDirectory, combined);
    }

    private static void CopyDirectory(
        string sourceRoot,
        string destinationRoot,
        Func<string, bool>? include = null,
        bool mergeIdenticalFiles = false)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        destinationRoot = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(sourceRoot);
        }
        if (IsReparsePoint(sourceRoot))
        {
            throw new InvalidDataException($"Payload source is a link or reparse point: {sourceRoot}");
        }
        Directory.CreateDirectory(destinationRoot);
        foreach (string file in EnumerateFilesSafe(sourceRoot))
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, file));
            if (include is not null && !include(relative))
            {
                continue;
            }
            string destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (mergeIdenticalFiles)
            {
                CopyFileMerged(file, destination);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (IsReparsePoint(directory))
            {
                throw new InvalidDataException($"Payload source contains a linked directory: {directory}");
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (IsReparsePoint(entry))
                {
                    throw new InvalidDataException($"Payload source contains a link or reparse point: {entry}");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
                else if (File.Exists(entry))
                {
                    yield return entry;
                }
                else
                {
                    throw new InvalidDataException($"Unsupported payload source entry: {entry}");
                }
            }
        }
    }

    private static void CopyFileMerged(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
            return;
        }
        if (!File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
        {
            throw new InvalidDataException($"Conflicting payload files map to '{destination}'.");
        }
    }

    private static string NormalizeRelativePath(string path) =>
        string.Join('/', path.Split(Path.DirectorySeparatorChar));

    private static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private sealed record SelectedModuleProject(string ProjectPath, string ModuleId, EngineModuleSide Side);
}

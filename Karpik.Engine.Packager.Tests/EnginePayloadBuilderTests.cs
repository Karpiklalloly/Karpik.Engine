using Karpik.Engine.Packager;
using Karpik.Engine.Tooling;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Xunit;

namespace Karpik.Engine.Packager.Tests;

public sealed class EnginePayloadBuilderTests
{
    [Fact]
    public void BuilderPublishesExactCompleteLayoutWithValidHash()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");

        EnginePayloadBuildResult result = new EnginePayloadBuilder().Build(source, output, "0.6.0", "0.6.0-sdk");
        EngineInstallationValidationResult validation = new EngineInstallationValidator()
            .Validate(result.DestinationDirectory, "0.6.0-sdk", "0.6.0");

        Assert.True(validation.IsValid, validation.Message);
        Assert.False(result.ReusedExistingInstallation);
        Assert.Equal(result.ContentHash, EngineContentHash.Compute(result.DestinationDirectory));
        Assert.Equal(
            [".complete", "editor", "engine-installation.json", "modules", "native", "runners", "sdk"],
            Directory.EnumerateFileSystemEntries(result.DestinationDirectory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
        Assert.Equal("0.6.0", validation.Manifest!.EditorVersion);
    }

    [Fact]
    public void BuilderStripsSymbolFilesFromPublishedPayload()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        File.WriteAllText(Path.Combine(source, "editor", "libSkiaSharp.pdb"), "symbols");
        File.WriteAllText(Path.Combine(source, "modules", "Module", "Helper.pdb"), "symbols");

        EnginePayloadBuildResult result = new EnginePayloadBuilder().Build(
            source,
            Path.Combine(temporary.RootPath, "output"),
            "0.6.0",
            "0.6.0-sdk");
        EngineInstallationValidationResult validation = new EngineInstallationValidator()
            .Validate(result.DestinationDirectory, "0.6.0-sdk", "0.6.0");

        Assert.True(validation.IsValid, validation.Message);
        Assert.Empty(Directory.EnumerateFiles(result.DestinationDirectory, "*.pdb", SearchOption.AllDirectories));
    }

    [Fact]
    public void BuilderStagesClientModuleNativeAssetsWhereClientRunnerCanDiscoverThem()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string moduleNative = Path.Combine(
            source,
            "modules",
            "Module",
            "runtimes",
            "win-x64",
            "native",
            "SDL2.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(moduleNative)!);
        File.WriteAllText(moduleNative, "client-sdl-x64");
        File.WriteAllText(
            Path.Combine(source, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize([
                new EngineModuleCatalogEntry("Module", EngineModuleSide.Client)
            ]));

        EnginePayloadBuildResult result = new EnginePayloadBuilder().Build(
            source,
            Path.Combine(temporary.RootPath, "output"),
            "0.6.0",
            "sdk");

        string clientNative = Path.Combine(
            result.DestinationDirectory,
            "runners",
            "client",
            "runtimes",
            "win-x64",
            "native",
            "SDL2.dll");
        string serverNative = Path.Combine(
            result.DestinationDirectory,
            "runners",
            "server",
            "runtimes",
            "win-x64",
            "native",
            "SDL2.dll");
        Assert.Equal("client-sdl-x64", File.ReadAllText(clientNative));
        Assert.False(File.Exists(serverNative));
    }

    [Fact]
    public void BuilderRejectsPreparedPayloadWithALegacyFlatModuleFile()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        File.WriteAllText(Path.Combine(source, "modules", "LegacyModule.dll"), "legacy");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new EnginePayloadBuilder().Build(source, output, "0.6.0", "sdk"));

        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(output, "Engines", "0.6.0")));
    }

    [Fact]
    public void BuilderRejectsPreparedPayloadWithAnUnsafeModuleDirectory()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        string unsafeModule = Path.Combine(source, "modules", "Unsafe Module");
        Directory.CreateDirectory(unsafeModule);
        File.WriteAllText(Path.Combine(unsafeModule, "Unsafe Module.dll"), "unsafe");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new EnginePayloadBuilder().Build(source, output, "0.6.0", "sdk"));

        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(output, "Engines", "0.6.0")));
    }

    [Fact]
    public void BuilderRejectsPreparedPayloadWithCaseCollidingModuleIdsWhenRepresentable()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string modulesRoot = Path.Combine(source, "modules");
        string collidingModule = Path.Combine(modulesRoot, "module");
        Directory.CreateDirectory(collidingModule);
        if (Directory.EnumerateDirectories(modulesRoot).Count() < 2)
        {
            Assert.True(ModuleLayoutPolicy.ModuleIdComparer.Equals("Module", "module"));
            return;
        }
        File.WriteAllText(Path.Combine(collidingModule, "module.dll"), "collision");
        string output = Path.Combine(temporary.RootPath, "output");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new EnginePayloadBuilder().Build(source, output, "0.6.0", "sdk"));

        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(output, "Engines", "0.6.0")));
    }

    [Fact]
    public void IdenticalRerunReusesExistingInstallationWithoutRewritingManifest()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(source, output, "0.6.0", "sdk");
        string manifestPath = Path.Combine(first.DestinationDirectory, PayloadLayout.ManifestFileName);
        DateTime timestamp = File.GetLastWriteTimeUtc(manifestPath);

        EnginePayloadBuildResult second = builder.Build(source, output, "0.6.0", "sdk");

        Assert.True(second.ReusedExistingInstallation);
        Assert.Equal(first.DestinationDirectory, second.DestinationDirectory);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void DifferentValidContentConflictsWithImmutableStableVersionAndPreservesDestination()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(source, output, "0.6.0", "sdk");
        File.WriteAllText(Path.Combine(source, "modules", "Module", "Module.dll"), "replacement");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(source, output, "0.6.0", "sdk"));

        Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("module", File.ReadAllText(Path.Combine(first.DestinationDirectory, "modules", "Module", "Module.dll")));
        Assert.True(new EngineInstallationValidator().Validate(first.DestinationDirectory).IsValid);
    }

    [Fact]
    public void ExistingValidStableVersionWithDifferentSdkIsStillImmutable()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(source, output, "0.6.0", "sdk-a");

        Assert.Throws<InvalidOperationException>(() => builder.Build(source, output, "0.6.0", "sdk-b"));

        EngineInstallationValidationResult preserved = new EngineInstallationValidator().Validate(first.DestinationDirectory);
        Assert.True(preserved.IsValid, preserved.Message);
        Assert.Equal("sdk-a", preserved.Manifest!.MsBuildSdkVersion);
    }

    [Fact]
    public void DifferentDevContentPublishesToHashQualifiedSideBySideDestinations()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(source, output, "0.6.0-dev", "sdk");
        File.WriteAllText(Path.Combine(source, "modules", "Module", "Module.dll"), "replacement");

        EnginePayloadBuildResult second = builder.Build(source, output, "0.6.0-dev", "sdk");

        Assert.NotEqual(first.DestinationDirectory, second.DestinationDirectory);
        Assert.EndsWith("0.6.0-dev-" + first.ContentHash, first.DestinationDirectory, StringComparison.Ordinal);
        Assert.EndsWith("0.6.0-dev-" + second.ContentHash, second.DestinationDirectory, StringComparison.Ordinal);
        Assert.True(Directory.Exists(first.DestinationDirectory));
        Assert.True(Directory.Exists(second.DestinationDirectory));
        Assert.Equal("module", File.ReadAllText(Path.Combine(first.DestinationDirectory, "modules", "Module", "Module.dll")));
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(second.DestinationDirectory, "modules", "Module", "Module.dll")));
    }

    [Fact]
    public void InvalidSourcePreservesExistingValidDestination()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(source, output, "0.6.0", "sdk");
        File.Delete(Path.Combine(source, "runners", "server", PayloadLayout.RunnerAssemblyFileName));

        Assert.ThrowsAny<Exception>(() => builder.Build(source, output, "0.6.0", "sdk"));
        EngineInstallationValidationResult validation = new EngineInstallationValidator()
            .Validate(first.DestinationDirectory, "sdk", "0.6.0");
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void BuilderRejectsVersionTraversalBeforeCreatingAnInstallation()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");

        Assert.Throws<ArgumentException>(() => new EnginePayloadBuilder().Build(source, output, "../escape", "sdk"));
        Assert.False(Directory.Exists(Path.Combine(temporary.RootPath, "escape")));
    }

    [Fact]
    public void RerunRecoversOwnedInterruptedStagingWithoutTouchingUnrelatedDirectory()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string source = PreparedPayload.Create(Path.Combine(temporary.RootPath, "source"));
        string output = Path.Combine(temporary.RootPath, "output");
        var publisher = new AtomicDirectoryPublisher(output);
        string abandoned = publisher.CreateStagingDirectory();
        File.WriteAllText(Path.Combine(abandoned, "partial"), "partial");
        string unrelated = Path.Combine(output, ".staging", "unrelated");
        Directory.CreateDirectory(unrelated);

        new EnginePayloadBuilder().Build(source, output, "0.6.0", "sdk");

        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void RepositoryModeIgnoresArbitraryStaleSourceBinAndModuleVersionDirectories()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string repository = FakeRepository.Create(Path.Combine(temporary.RootPath, "repository"));
        string output = Path.Combine(temporary.RootPath, "output");

        var builder = new EnginePayloadBuilder();
        EnginePayloadBuildResult first = builder.Build(repository, output, "0.6.0", "0.6.0-sdk");
        string[] payloadPaths = Directory.EnumerateFileSystemEntries(first.DestinationDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(first.DestinationDirectory, path).Replace('\\', '/'))
            .ToArray();
        IReadOnlyDictionary<string, string> firstSnapshot = SnapshotFiles(first.DestinationDirectory);
        IReadOnlyDictionary<string, string> firstPackageSnapshot = SnapshotPackage(first.DestinationDirectory);
        EnginePayloadBuildResult second = builder.Build(repository, output, "0.6.0", "0.6.0-sdk");
        string[] secondPayloadPaths = Directory.EnumerateFileSystemEntries(second.DestinationDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(second.DestinationDirectory, path).Replace('\\', '/'))
            .ToArray();
        IReadOnlyDictionary<string, string> secondSnapshot = SnapshotFiles(second.DestinationDirectory);
        IReadOnlyDictionary<string, string> secondPackageSnapshot = SnapshotPackage(second.DestinationDirectory);
        string[] differences = firstSnapshot.Keys.Union(secondSnapshot.Keys, StringComparer.Ordinal)
            .Where(path => !firstSnapshot.TryGetValue(path, out string? firstHash) ||
                           !secondSnapshot.TryGetValue(path, out string? secondHash) ||
                           !string.Equals(firstHash, secondHash, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] packageDifferences = firstPackageSnapshot.Keys.Union(secondPackageSnapshot.Keys, StringComparer.Ordinal)
            .Where(path => !firstPackageSnapshot.TryGetValue(path, out string? firstHash) ||
                           !secondPackageSnapshot.TryGetValue(path, out string? secondHash) ||
                           !string.Equals(firstHash, secondHash, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(payloadPaths, path => path.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(payloadPaths, path => path.Contains("modules.version.", StringComparison.Ordinal));
        string moduleA = Path.Combine(first.DestinationDirectory, "modules", "TestModuleA");
        string moduleB = Path.Combine(first.DestinationDirectory, "modules", "TestModuleB");
        Assert.True(File.Exists(Path.Combine(moduleA, "TestModuleA.dll")));
        Assert.True(File.Exists(Path.Combine(moduleB, "TestModuleB.dll")));
        Assert.Equal(
            "Shared\tTestModuleA\nShared\tTestModuleB\n",
            File.ReadAllText(Path.Combine(first.DestinationDirectory, "modules", EngineModuleCatalog.FileName)));
        Assert.True(File.Exists(Path.Combine(moduleA, "SharedDependency.dll")));
        Assert.True(File.Exists(Path.Combine(moduleB, "SharedDependency.dll")));
        Assert.True(
            File.ReadAllBytes(Path.Combine(moduleA, "SharedDependency.dll"))
                .AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(moduleB, "SharedDependency.dll"))));
        AssertPackageContains(first.DestinationDirectory, "tools/net10.0/Karpik.Engine.Sdk.Tasks.dll");
        AssertPackageContains(first.DestinationDirectory, "tools/net10.0/Karpik.Engine.ProjectModel.dll");
        AssertPackageContains(first.DestinationDirectory, "tools/net10.0/Karpik.Engine.Tooling.dll");
        AssertPackageContains(first.DestinationDirectory, "analyzers/dotnet/cs/Karpik.Engine.Core.Codegen.dll");
        AssertPackageAssemblyContainsType(
            first.DestinationDirectory,
            "analyzers/dotnet/cs/Network.Codegen.dll",
            "NetworkCodegenFreshMarker");
        Assert.True(second.ReusedExistingInstallation,
            $"Changed payload files: {string.Join(", ", differences)}; changed package entries: {string.Join(", ", packageDifferences)}");
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(payloadPaths.Length, secondPayloadPaths.Length);
    }

    [Fact]
    public void RepositoryModeStagesClientModuleNativeAssetsForClientRunnerOnly()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string repository = FakeRepository.Create(
            Path.Combine(temporary.RootPath, "repository"),
            includeClientNativeModule: true);

        EnginePayloadBuildResult result = new EnginePayloadBuilder().Build(
            repository,
            Path.Combine(temporary.RootPath, "output"),
            "0.6.0",
            "0.6.0-sdk");

        string relativeNative = Path.Combine("runtimes", "win-x64", "native", "SDL2.dll");
        Assert.Equal(
            "client-sdl-x64",
            File.ReadAllText(Path.Combine(result.DestinationDirectory, "runners", "client", relativeNative)));
        Assert.False(File.Exists(Path.Combine(result.DestinationDirectory, "runners", "server", relativeNative)));
    }

    [Fact]
    public void RepositoryModeRejectsByteDistinctAssembliesWithTheSameIdentity()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string repository = FakeRepository.Create(
            Path.Combine(temporary.RootPath, "repository"),
            conflictingDependencies: true);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new EnginePayloadBuilder().Build(repository, Path.Combine(temporary.RootPath, "output"), "0.6.0", "0.6.0-sdk"));

        Assert.Contains("same identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryModeRejectsDifferentVersionsWithTheSameAssemblyName()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string repository = FakeRepository.Create(
            Path.Combine(temporary.RootPath, "repository"),
            versionedDependencies: true);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new EnginePayloadBuilder().Build(repository, Path.Combine(temporary.RootPath, "output"), "0.6.0", "0.6.0-sdk"));

        Assert.Contains("same simple name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> SnapshotFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SnapshotPackage(string root)
    {
        string packagePath = Directory.EnumerateFiles(Path.Combine(root, "sdk"), "*.nupkg").Single();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        return package.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using Stream stream = entry.Open();
                return Convert.ToHexString(SHA256.HashData(stream));
            },
            StringComparer.Ordinal);
    }

    private static void AssertPackageContains(string root, string entryName)
    {
        string packagePath = Directory.EnumerateFiles(Path.Combine(root, "sdk"), "*.nupkg").Single();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        Assert.NotNull(package.GetEntry(entryName));
    }

    private static void AssertPackageAssemblyContainsType(
        string root,
        string entryName,
        string expectedTypeName)
    {
        string packagePath = Directory.EnumerateFiles(Path.Combine(root, "sdk"), "*.nupkg").Single();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(package.GetEntry(entryName));
        using Stream stream = entry.Open();
        using var assembly = new MemoryStream();
        stream.CopyTo(assembly);
        assembly.Position = 0;
        using var pe = new PEReader(assembly);
        Assert.True(pe.HasMetadata);
        MetadataReader metadata = pe.GetMetadataReader();
        Assert.Contains(
            metadata.TypeDefinitions,
            handle => string.Equals(
                metadata.GetString(metadata.GetTypeDefinition(handle).Name),
                expectedTypeName,
                StringComparison.Ordinal));
    }

}

internal static class PreparedPayload
{
    public static string Create(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "editor"));
        Directory.CreateDirectory(Path.Combine(root, "sdk"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
        Directory.CreateDirectory(Path.Combine(root, "modules", "Module"));
        Directory.CreateDirectory(Path.Combine(root, "native"));
        File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), "editor");
        File.WriteAllText(Path.Combine(root, "sdk", "Karpik.Engine.Sdk.nupkg"), "sdk");
        File.WriteAllText(Path.Combine(root, "runners", "client", PayloadLayout.RunnerAssemblyFileName), "client");
        File.WriteAllText(Path.Combine(root, "runners", "server", PayloadLayout.RunnerAssemblyFileName), "server");
        File.WriteAllText(Path.Combine(root, "modules", "Module", "Module.dll"), "module");
        File.WriteAllText(
            Path.Combine(root, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize([
                new EngineModuleCatalogEntry("Module", EngineModuleSide.Shared)
            ]));
        return root;
    }
}

internal sealed class PackagerTemporaryDirectory : IDisposable
{
    public PackagerTemporaryDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "karpik-packager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class FakeRepository
{
    public static string Create(
        string root,
        bool conflictingDependencies = false,
        bool versionedDependencies = false,
        bool includeClientNativeModule = false)
    {
        Directory.CreateDirectory(root);
        WriteProject(root, "Karpik.Editor/Karpik.Editor.csproj", "Karpik.Editor");
        WriteProject(root, "Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj", "Karpik.Engine.Core.Runner");
        WriteProject(root, "Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj", "Karpik.Engine.ProjectModel");
        WriteProject(
            root,
            "Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/Karpik.Engine.Core.Codegen.csproj",
            "Karpik.Engine.Core.Codegen");
        WriteProject(
            root,
            "Network.Codegen/Network.Codegen/Network.Codegen.csproj",
            "Network.Codegen",
            markerSource: "public static class NetworkCodegenFreshMarker { }");
        WriteProject(root, "Karpik.Content.Tool/Karpik.Content.Tool.csproj", "Karpik.Content.Tool");
        WriteProject(root, "Karpik.Content.Codegen/Karpik.Content.Codegen.csproj", "Karpik.Content.Codegen");
        WriteProject(root, "Karpik.Content.Runtime/Karpik.Content.Runtime.csproj", "Karpik.Content.Runtime");
        WriteProject(
            root,
            "Karpik.Engine.Tooling/Karpik.Engine.Tooling.csproj",
            "Karpik.Engine.Tooling",
            "../Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj");
        WriteProject(
            root,
            "Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj",
            "Karpik.Engine.Sdk.Tasks",
            "../Karpik.Engine.Tooling/Karpik.Engine.Tooling.csproj");
        WriteProject(
            root,
            "Dependencies/A/SharedDependencyA.csproj",
            "SharedDependency",
            markerSource: "internal static class DependencyMarker { internal const int Value = 1; }",
            assemblyVersion: versionedDependencies ? "1.0.0.0" : null);
        WriteProject(
            root,
            "Dependencies/B/SharedDependencyB.csproj",
            "SharedDependency",
            markerSource: conflictingDependencies
                ? "internal static class DependencyMarker { internal const int Value = 2; }"
                : "internal static class DependencyMarker { internal const int Value = 1; }",
            assemblyVersion: versionedDependencies ? "2.0.0.0" : null);
        WriteProject(root, "Modules/Shared/TestModuleA/TestModuleA.csproj", "TestModuleA", "../../../Dependencies/A/SharedDependencyA.csproj");
        WriteProject(root, "Modules/Shared/TestModuleB/TestModuleB.csproj", "TestModuleB", "../../../Dependencies/B/SharedDependencyB.csproj");
        if (includeClientNativeModule)
        {
            WriteProject(
                root,
                "Modules/Client/NativeClient/NativeClient.csproj",
                "NativeClient",
                nativeAsset: true);
        }
        WriteProject(root, "Karpik.Engine.Client.Publish/Karpik.Engine.Client.Publish.csproj", "Karpik.Engine.Client.Publish", "../Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj");
        WriteProject(root, "Karpik.Engine.Server.Publish/Karpik.Engine.Server.Publish.csproj", "Karpik.Engine.Server.Publish", "../Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj");
        string sdkProject = Path.Combine(root, "Karpik.Engine.Sdk", "Karpik.Engine.Sdk.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(sdkProject)!);
        File.WriteAllText(sdkProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Karpik.Engine.Sdk</PackageId>
                <PackageVersion>0.6.0-sdk</PackageVersion>
                <KarpikSdkTasksOutputPath Condition="'$(KarpikSdkTasksOutputPath)' == ''">..\Karpik.Engine.Sdk.Tasks\bin\$(Configuration)\net10.0\</KarpikSdkTasksOutputPath>
                <KarpikCoreCodegenOutputPath Condition="'$(KarpikCoreCodegenOutputPath)' == ''">..\Karpik.Engine.Core.Generator\Karpik.Engine.Core.Codegen\bin\$(Configuration)\net10.0\</KarpikCoreCodegenOutputPath>
                <KarpikNetworkCodegenOutputPath Condition="'$(KarpikNetworkCodegenOutputPath)' == ''">..\Network.Codegen\Network.Codegen\bin\$(Configuration)\net10.0\</KarpikNetworkCodegenOutputPath>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Karpik.Engine.Sdk.Tasks\Karpik.Engine.Sdk.Tasks.csproj"
                                  PrivateAssets="all"
                                  ReferenceOutputAssembly="false" />
                <None Include="$(KarpikSdkTasksOutputPath)Karpik.Engine.Sdk.Tasks.dll"
                      Pack="true" PackagePath="tools/net10.0/" />
                <None Include="$(KarpikSdkTasksOutputPath)Karpik.Engine.ProjectModel.dll"
                      Pack="true" PackagePath="tools/net10.0/" />
                <None Include="$(KarpikSdkTasksOutputPath)Karpik.Engine.Tooling.dll"
                      Pack="true" PackagePath="tools/net10.0/" />
                <None Include="$(KarpikCoreCodegenOutputPath)Karpik.Engine.Core.Codegen.dll"
                      Pack="true" PackagePath="analyzers/dotnet/cs/" />
                <None Include="$(KarpikNetworkCodegenOutputPath)Network.Codegen.dll"
                      Pack="true" PackagePath="analyzers/dotnet/cs/" />
                <ProjectReference Include="..\Network.Codegen\Network.Codegen\Network.Codegen.csproj"
                                  PrivateAssets="all"
                                  ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);
        string clientNativeReference = includeClientNativeModule
            ? "<PluginReference Include=\"$(MSBuildThisFileDirectory)Modules/Client/NativeClient/NativeClient.csproj\" />"
            : string.Empty;
        File.WriteAllText(Path.Combine(root, "AutoGenerated.targets"), $$"""
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PluginReference Include="$(MSBuildThisFileDirectory)Modules/Shared/TestModuleA/TestModuleA.csproj" />
                <PluginReference Include="$(MSBuildThisFileDirectory)Modules/Shared/TestModuleB/TestModuleB.csproj" />
                {{clientNativeReference}}
              </ItemGroup>
            </Project>
            """);
        string clientNativeSolutionProject = includeClientNativeModule
            ? "<Project Path=\"Modules/Client/NativeClient/NativeClient.csproj\" />"
            : string.Empty;
        File.WriteAllText(Path.Combine(root, "KarpikEngine.slnx"), $$"""
            <Solution>
              <Project Path="Karpik.Editor/Karpik.Editor.csproj" />
              <Project Path="Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj" />
              <Project Path="Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj" />
              <Project Path="Karpik.Engine.Tooling/Karpik.Engine.Tooling.csproj" />
              <Project Path="Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/Karpik.Engine.Core.Codegen.csproj" />
              <Project Path="Network.Codegen/Network.Codegen/Network.Codegen.csproj" />
              <Project Path="Karpik.Content.Tool/Karpik.Content.Tool.csproj" />
              <Project Path="Karpik.Content.Codegen/Karpik.Content.Codegen.csproj" />
              <Project Path="Karpik.Content.Runtime/Karpik.Content.Runtime.csproj" />
              <Project Path="Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj" />
              <Project Path="Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj" />
              <Project Path="Dependencies/A/SharedDependencyA.csproj" />
              <Project Path="Dependencies/B/SharedDependencyB.csproj" />
              <Project Path="Modules/Shared/TestModuleA/TestModuleA.csproj" />
              <Project Path="Modules/Shared/TestModuleB/TestModuleB.csproj" />
              {{clientNativeSolutionProject}}
              <Project Path="Karpik.Engine.Client.Publish/Karpik.Engine.Client.Publish.csproj" />
              <Project Path="Karpik.Engine.Server.Publish/Karpik.Engine.Server.Publish.csproj" />
            </Solution>
            """);

        WriteStaleFile(root, "Karpik.Editor/bin/Release/net10.0/stale-editor.txt");
        WriteStaleFile(root, "Karpik.Engine.Client.Publish/bin/Release/net10.0/stale-runner.txt");
        WriteStaleFile(root, "Karpik.Engine.Client.Publish/bin/Release/net10.0/runtimes/win-x64/native/stale-native.dll");
        WriteStaleFile(root, "Karpik.Engine.Client.Publish/bin/Release/net10.0/modules.version.stale/Module.dll");
        WriteStaleFile(root, "Karpik.Engine.Client.Publish/bin/Release/net10.0/modules.version.stale/.complete");
        WriteStaleFile(root, "Karpik.Engine.Server.Publish/bin/Release/net10.0/modules.version.stale/Module.dll");
        WriteStaleFile(root, "Karpik.Engine.Server.Publish/bin/Release/net10.0/modules.version.stale/.complete");
        WriteStaleFile(root, "Network.Codegen/Network.Codegen/bin/Release/net10.0/Network.Codegen.dll");
        return root;
    }

    private static void WriteProject(
        string root,
        string relativePath,
        string assemblyName,
        string? projectReference = null,
        string? markerSource = null,
        string? assemblyVersion = null,
        bool nativeAsset = false)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string reference = projectReference is null
            ? string.Empty
            : $"<ItemGroup><ProjectReference Include=\"{projectReference}\" /></ItemGroup>";
        string version = assemblyVersion is null
            ? string.Empty
            : $"<AssemblyVersion>{assemblyVersion}</AssemblyVersion>";
        string nativeItem = nativeAsset
            ? "<ItemGroup><None Update=\"runtimes/win-x64/native/SDL2.dll\" CopyToOutputDirectory=\"PreserveNewest\" /></ItemGroup>"
            : string.Empty;
        File.WriteAllText(path, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                {{version}}
              </PropertyGroup>
              {{reference}}
              {{nativeItem}}
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(path)!, "Marker.cs"),
            (markerSource ?? "internal static class Marker { }") + Environment.NewLine);
        if (nativeAsset)
        {
            string nativePath = Path.Combine(Path.GetDirectoryName(path)!, "runtimes", "win-x64", "native", "SDL2.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            File.WriteAllText(nativePath, "client-sdl-x64");
        }
    }

    private static void WriteStaleFile(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stale");
    }
}

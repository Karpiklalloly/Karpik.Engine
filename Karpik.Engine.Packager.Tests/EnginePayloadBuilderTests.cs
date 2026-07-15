using Karpik.Engine.Packager;
using Karpik.Engine.Tooling;
using System.IO.Compression;
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
        Assert.True(File.Exists(Path.Combine(moduleA, "SharedDependency.dll")));
        Assert.True(File.Exists(Path.Combine(moduleB, "SharedDependency.dll")));
        Assert.False(
            File.ReadAllBytes(Path.Combine(moduleA, "SharedDependency.dll"))
                .AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(moduleB, "SharedDependency.dll"))));
        AssertPackageContains(first.DestinationDirectory, "tools/net10.0/Karpik.Engine.Sdk.Tasks.dll");
        AssertPackageContains(first.DestinationDirectory, "tools/net10.0/Karpik.Engine.ProjectModel.dll");
        Assert.True(second.ReusedExistingInstallation,
            $"Changed payload files: {string.Join(", ", differences)}; changed package entries: {string.Join(", ", packageDifferences)}");
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(payloadPaths.Length, secondPayloadPaths.Length);
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
    public static string Create(string root)
    {
        Directory.CreateDirectory(root);
        WriteProject(root, "Karpik.Editor/Karpik.Editor.csproj", "Karpik.Editor");
        WriteProject(root, "Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj", "Karpik.Engine.Core.Runner");
        WriteProject(root, "Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj", "Karpik.Engine.ProjectModel");
        WriteProject(
            root,
            "Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj",
            "Karpik.Engine.Sdk.Tasks",
            "../Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj");
        WriteProject(root, "Dependencies/A/SharedDependencyA.csproj", "SharedDependency", markerSource: "internal static class DependencyMarker { internal const int Value = 1; }");
        WriteProject(root, "Dependencies/B/SharedDependencyB.csproj", "SharedDependency", markerSource: "internal static class DependencyMarker { internal const int Value = 2; }");
        WriteProject(root, "Modules/Shared/TestModuleA/TestModuleA.csproj", "TestModuleA", "../../../Dependencies/A/SharedDependencyA.csproj");
        WriteProject(root, "Modules/Shared/TestModuleB/TestModuleB.csproj", "TestModuleB", "../../../Dependencies/B/SharedDependencyB.csproj");
        WriteProject(root, "ClientLauncher/ClientLauncher.csproj", "ClientLauncher", "../Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj");
        WriteProject(root, "ServerLauncher/ServerLauncher.csproj", "ServerLauncher", "../Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj");
        string sdkProject = Path.Combine(root, "Karpik.Engine.Sdk", "Karpik.Engine.Sdk.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(sdkProject)!);
        File.WriteAllText(sdkProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Karpik.Engine.Sdk</PackageId>
                <PackageVersion>0.6.0-sdk</PackageVersion>
                <KarpikSdkTasksOutputPath Condition="'$(KarpikSdkTasksOutputPath)' == ''">..\Karpik.Engine.Sdk.Tasks\bin\$(Configuration)\net10.0\</KarpikSdkTasksOutputPath>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Karpik.Engine.Sdk.Tasks\Karpik.Engine.Sdk.Tasks.csproj"
                                  PrivateAssets="all"
                                  ReferenceOutputAssembly="false" />
                <None Include="$(KarpikSdkTasksOutputPath)Karpik.Engine.Sdk.Tasks.dll"
                      Pack="true" PackagePath="tools/net10.0/" />
                <None Include="$(KarpikSdkTasksOutputPath)Karpik.Engine.ProjectModel.dll"
                      Pack="true" PackagePath="tools/net10.0/" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "AutoGenerated.targets"), """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PluginReference Include="$(MSBuildThisFileDirectory)Modules/Shared/TestModuleA/TestModuleA.csproj" />
                <PluginReference Include="$(MSBuildThisFileDirectory)Modules/Shared/TestModuleB/TestModuleB.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "KarpikEngine.slnx"), """
            <Solution>
              <Project Path="Karpik.Editor/Karpik.Editor.csproj" />
              <Project Path="Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj" />
              <Project Path="Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj" />
              <Project Path="Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj" />
              <Project Path="Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj" />
              <Project Path="Dependencies/A/SharedDependencyA.csproj" />
              <Project Path="Dependencies/B/SharedDependencyB.csproj" />
              <Project Path="Modules/Shared/TestModuleA/TestModuleA.csproj" />
              <Project Path="Modules/Shared/TestModuleB/TestModuleB.csproj" />
              <Project Path="ClientLauncher/ClientLauncher.csproj" />
              <Project Path="ServerLauncher/ServerLauncher.csproj" />
            </Solution>
            """);

        WriteStaleFile(root, "Karpik.Editor/bin/Release/net10.0/stale-editor.txt");
        WriteStaleFile(root, "ClientLauncher/bin/Release/net10.0/stale-runner.txt");
        WriteStaleFile(root, "ClientLauncher/bin/Release/net10.0/runtimes/win-x64/native/stale-native.dll");
        WriteStaleFile(root, "ClientLauncher/bin/Release/net10.0/modules.version.stale/Module.dll");
        WriteStaleFile(root, "ClientLauncher/bin/Release/net10.0/modules.version.stale/.complete");
        WriteStaleFile(root, "ServerLauncher/bin/Release/net10.0/modules.version.stale/Module.dll");
        WriteStaleFile(root, "ServerLauncher/bin/Release/net10.0/modules.version.stale/.complete");
        return root;
    }

    private static void WriteProject(
        string root,
        string relativePath,
        string assemblyName,
        string? projectReference = null,
        string? markerSource = null)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string reference = projectReference is null
            ? string.Empty
            : $"<ItemGroup><ProjectReference Include=\"{projectReference}\" /></ItemGroup>";
        File.WriteAllText(path, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{assemblyName}}</AssemblyName>
              </PropertyGroup>
              {{reference}}
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(path)!, "Marker.cs"),
            (markerSource ?? "internal static class Marker { }") + Environment.NewLine);
    }

    private static void WriteStaleFile(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stale");
    }
}

using System.Collections;
using System.Text;
using System.Xml.Linq;
using Karpik.Engine.Sdk.Tasks;
using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;
using Xunit;
using Xunit.Sdk;

namespace Karpik.Engine.Sdk.Tasks.Tests;

public sealed class ResolveKarpikStaticReferencesTaskTests
{
    [Fact]
    public void Sdk_WiresStaticModuleReferencesBeforeAssemblyResolution()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement usingTask = targets.Root!.Elements("UsingTask")
            .Single(element => (string?)element.Attribute("TaskName") ==
                               "Karpik.Engine.Sdk.Tasks.ResolveKarpikStaticReferencesTask");
        XElement target = targets.Root.Elements("Target")
            .Single(element => (string?)element.Attribute("Name") == "_KarpikResolveStaticModuleReferences");
        XElement resolve = Assert.Single(target.Elements("ResolveKarpikStaticReferencesTask"));
        XElement output = Assert.Single(resolve.Elements("Output"));
        XElement reference = Assert.Single(target.Descendants("Reference"));

        Assert.Equal("$(_KarpikSdkTaskAssembly)", (string?)usingTask.Attribute("AssemblyFile"));
        Assert.Equal("ResolveAssemblyReferences", (string?)target.Attribute("BeforeTargets"));
        string condition = Assert.IsType<XAttribute>(target.Attribute("Condition")).Value;
        Assert.Contains("'$(KarpikCompositionMode)' == 'Static'", condition);
        Assert.Contains("'$(KarpikProjectKind)' == 'Runtime'", condition);
        Assert.Contains("'$(KarpikProjectKind)' == 'Tool'", condition);
        Assert.Equal("$(KarpikEngineRoot)", (string?)resolve.Attribute("EngineRoot"));
        Assert.Equal("$(KarpikSide)", (string?)resolve.Attribute("Side"));
        Assert.Equal("References", (string?)output.Attribute("TaskParameter"));
        Assert.Equal("_KarpikStaticModuleReference", (string?)output.Attribute("ItemName"));
        Assert.Equal("%(_KarpikStaticModuleReference.Filename)", (string?)reference.Attribute("Include"));
        Assert.Equal("%(_KarpikStaticModuleReference.Identity)", reference.Element("HintPath")?.Value);
        Assert.Equal("false", reference.Element("Private")?.Value);
    }

    [Theory]
    [InlineData("Shared", new[] { "Karpik.Engine.Shared.A", "Karpik.Engine.Shared.B" })]
    [InlineData("Client", new[] { "Karpik.Engine.Shared.A", "Karpik.Engine.Shared.B", "Karpik.Engine.Client.A" })]
    [InlineData("Server", new[] { "Karpik.Engine.Shared.A", "Karpik.Engine.Shared.B", "Karpik.Engine.Server.A" })]
    public void Execute_ResolvesOnlyTheCanonicalModulesForTheRequestedSide(string side, string[] expectedModules)
    {
        using var installation = new TemporaryInstallation();
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.B");
        installation.AddModule(EngineModuleSide.Server, "Karpik.Engine.Server.A");
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.A");
        installation.AddModule(EngineModuleSide.Client, "Karpik.Engine.Client.A");
        installation.WriteCatalog();

        var engine = new FakeBuildEngine();
        var task = new ResolveKarpikStaticReferencesTask
        {
            BuildEngine = engine,
            EngineRoot = installation.Root,
            Side = side
        };

        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
        Assert.Equal(
            expectedModules,
            task.References.Select(reference => Path.GetFileNameWithoutExtension(reference.ItemSpec)).ToArray());
        Assert.All(task.References, reference =>
        {
            Assert.True(Path.IsPathFullyQualified(reference.ItemSpec));
            Assert.True(IsWithinRoot(reference.ItemSpec, Path.Combine(installation.Root, "modules")));
        });
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("shared")]
    public void Execute_RejectsInvalidSide(string side)
    {
        using var installation = new TemporaryInstallation();
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.A");
        installation.WriteCatalog();

        AssertFailure(installation.Root, side);
    }

    [Fact]
    public void Execute_RejectsMissingCatalog()
    {
        using var installation = new TemporaryInstallation();
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.A");

        AssertFailure(installation.Root, "Shared");
    }

    [Fact]
    public void Execute_RejectsMissingPrimaryAssembly()
    {
        using var installation = new TemporaryInstallation();
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.A", createAssembly: false);
        installation.WriteCatalog();

        AssertFailure(installation.Root, "Shared");
    }

    [Fact]
    public void Execute_RejectsUnsafeCatalogModuleIdBeforeResolvingPaths()
    {
        using var installation = new TemporaryInstallation();
        Directory.CreateDirectory(Path.Combine(installation.Root, "modules"));
        File.WriteAllText(
            Path.Combine(installation.Root, "modules", EngineModuleCatalog.FileName),
            "Shared\t../outside\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertFailure(installation.Root, "Shared");
        Assert.False(File.Exists(Path.Combine(installation.Root, "outside.dll")));
    }

    [Fact]
    public void Execute_RejectsLinkedModuleDirectoryWhenSymbolicLinksAreAvailable()
    {
        using var installation = new TemporaryInstallation();
        installation.AddModule(EngineModuleSide.Shared, "Karpik.Engine.Shared.A", createAssembly: false);
        installation.WriteCatalog();
        string modules = Path.Combine(installation.Root, "modules");
        string moduleDirectory = Path.Combine(modules, "Karpik.Engine.Shared.A");
        string target = Path.Combine(installation.Root, "linked-target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Karpik.Engine.Shared.A.dll"), "assembly");

        try
        {
            Directory.CreateSymbolicLink(moduleDirectory, target);
        }
        catch (PlatformNotSupportedException exception)
        {
            throw SkipException.ForSkip($"Symbolic links are not supported on this platform: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception) when (IsWindowsSymbolicLinkPrivilegeFailure(exception))
        {
            throw SkipException.ForSkip($"Creating symbolic links requires a Windows privilege that is unavailable: {exception.Message}");
        }
        catch (IOException exception) when (IsWindowsSymbolicLinkPrivilegeFailure(exception))
        {
            throw SkipException.ForSkip($"Creating symbolic links requires a Windows privilege that is unavailable: {exception.Message}");
        }

        AssertFailure(installation.Root, "Shared");
    }

    private static void AssertFailure(string engineRoot, string side)
    {
        var engine = new FakeBuildEngine();
        var task = new ResolveKarpikStaticReferencesTask
        {
            BuildEngine = engine,
            EngineRoot = engineRoot,
            Side = side
        };

        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
        Assert.Empty(task.References);
    }

    private static bool IsWithinRoot(string candidate, string root)
    {
        string fullCandidate = Path.GetFullPath(candidate);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsWindowsSymbolicLinkPrivilegeFailure(Exception exception) =>
        OperatingSystem.IsWindows() && exception.HResult == unchecked((int)0x80070522);

    private sealed class TemporaryInstallation : IDisposable
    {
        private readonly List<EngineModuleCatalogEntry> _entries = [];

        public TemporaryInstallation()
        {
            Root = Path.Combine(Path.GetTempPath(), "KarpikStaticReferenceTaskTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "modules"));
        }

        public string Root { get; }

        public void AddModule(EngineModuleSide side, string moduleId, bool createAssembly = true)
        {
            _entries.Add(new EngineModuleCatalogEntry(moduleId, side));
            if (!createAssembly)
            {
                return;
            }

            string directory = Path.Combine(Root, "modules", moduleId);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ModuleLayoutPolicy.GetPrimaryAssemblyFileName(moduleId)), "assembly");
        }

        public void WriteCatalog() => File.WriteAllText(
            Path.Combine(Root, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize(_entries),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
    }
}

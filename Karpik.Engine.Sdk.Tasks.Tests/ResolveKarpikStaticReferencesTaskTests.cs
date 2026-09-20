using System.Collections;
using System.Diagnostics;
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
    public void Sdk_UsesClrIdentityForStaticReferences()
    {
        var targets = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"));
        XElement target = targets.Root!.Elements("Target").Single(x => (string?)x.Attribute("Name") == "_KarpikResolveStaticModuleReferences");
        XElement catalogReference = target.Descendants("Reference")
            .Single(reference => (string?)reference.Attribute("Include") == "%(_KarpikStaticModuleReference.AssemblyIdentity)");
        XElement openTkReference = target.Descendants("Reference")
            .Single(reference => (string?)reference.Attribute("Include") == "OpenTK.Mathematics");

        Assert.Equal("%(_KarpikStaticModuleReference.Identity)", catalogReference.Element("HintPath")?.Value);
        Assert.Equal(
            @"$(KarpikEngineRoot)\modules\Spatial2D\OpenTK.Mathematics.dll",
            openTkReference.Element("HintPath")?.Value);
        Assert.Equal("false", openTkReference.Element("Private")?.Value);
    }

    [Fact]
    public void Execute_DeduplicatesSameClrIdentityInCanonicalOrder()
    {
        using var tree = new Tree();
        tree.Add("A", typeof(EngineModuleCatalog).Assembly.Location);
        tree.Add("B", typeof(EngineModuleCatalog).Assembly.Location);
        tree.WriteCatalog();
        var engine = new Engine();
        var task = new ResolveKarpikStaticReferencesTask { BuildEngine = engine, EngineRoot = tree.Root, Side = "Shared" };
        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
        Assert.Equal("A", Path.GetFileNameWithoutExtension(Assert.Single(task.References).ItemSpec));
        Assert.Equal(typeof(EngineModuleCatalog).Assembly.FullName, task.References[0].GetMetadata("AssemblyIdentity"));
    }

    [Fact]
    public void Execute_RejectsDifferentClrIdentitiesWithTheSameSimpleName()
    {
        using var tree = new Tree();
        tree.Add("A", Build(tree, "first", "Duplicate", "1.0.0.0"));
        tree.Add("B", Build(tree, "second", "Duplicate", "2.0.0.0"));
        tree.WriteCatalog();
        var engine = new Engine();
        var task = new ResolveKarpikStaticReferencesTask { BuildEngine = engine, EngineRoot = tree.Root, Side = "Shared" };
        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
        Assert.Empty(task.References);
    }

    [Fact]
    public void Execute_CollectsPayloadAssembliesFromSharedDirectory()
    {
        using var tree = new Tree();
        tree.Add("A", typeof(EngineModuleCatalog).Assembly.Location);
        tree.WriteCatalog();
        Directory.CreateDirectory(Path.Combine(tree.Root, "shared"));
        File.Copy(
            typeof(ResolveKarpikStaticReferencesTask).Assembly.Location,
            Path.Combine(tree.Root, "shared", "SharedDep.dll"));
        var task = new ResolveKarpikStaticReferencesTask { BuildEngine = new Engine(), EngineRoot = tree.Root, Side = "Shared" };

        Assert.True(task.Execute());
        Assert.Contains(
            task.PayloadAssemblies,
            item => item.ItemSpec.EndsWith("SharedDep.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ExcludesCatalogPrimaryAssemblyFromSharedPayload()
    {
        using var tree = new Tree();
        tree.Add("SharedModule", typeof(EngineModuleCatalog).Assembly.Location, EngineModuleSide.Shared);
        tree.Add("ClientModule", typeof(ResolveKarpikStaticReferencesTask).Assembly.Location, EngineModuleSide.Client);
        tree.WriteCatalog();

        string clientPrimary = Path.Combine(tree.Root, "modules", "ClientModule", "ClientModule.dll");
        Directory.CreateDirectory(Path.Combine(tree.Root, "shared"));
        File.Copy(clientPrimary, Path.Combine(tree.Root, "shared", "ClientModule.dll"));
        File.Copy(
            typeof(ResolveKarpikStaticReferencesTask).Assembly.Location,
            Path.Combine(tree.Root, "shared", "OrdinarySharedDependency.dll"));

        var task = new ResolveKarpikStaticReferencesTask
        {
            BuildEngine = new Engine(),
            EngineRoot = tree.Root,
            Side = "Server"
        };

        Assert.True(task.Execute());
        Assert.DoesNotContain(task.PayloadAssemblies, item =>
            item.ItemSpec.EndsWith("ClientModule.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(task.PayloadAssemblies, item =>
            item.ItemSpec.EndsWith("OrdinarySharedDependency.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Shared", 1)] [InlineData("Client", 2)] [InlineData("Server", 2)]
    public void Execute_RespectsSide(string side, int count)
    {
        using var tree = new Tree();
        tree.Add("Shared", typeof(EngineModuleCatalog).Assembly.Location, EngineModuleSide.Shared);
        tree.Add("Client", typeof(ResolveKarpikStaticReferencesTask).Assembly.Location, EngineModuleSide.Client);
        tree.Add("Server", typeof(IBuildEngine).Assembly.Location, EngineModuleSide.Server);
        tree.WriteCatalog();
        var task = new ResolveKarpikStaticReferencesTask { BuildEngine = new Engine(), EngineRoot = tree.Root, Side = side };
        Assert.True(task.Execute()); Assert.Equal(count, task.References.Length);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("shared")]
    public void Execute_RejectsInvalidSide(string side)
    {
        using var tree = new Tree();
        tree.Add("Shared", typeof(EngineModuleCatalog).Assembly.Location);
        tree.WriteCatalog();
        AssertFailure(tree.Root, side);
    }

    [Fact]
    public void Execute_RejectsMissingCatalog()
    {
        using var tree = new Tree();
        tree.Add("Shared", typeof(EngineModuleCatalog).Assembly.Location);
        AssertFailure(tree.Root, "Shared");
    }

    [Fact]
    public void Execute_RejectsMissingPrimaryAssembly()
    {
        using var tree = new Tree();
        tree.AddMissing("Shared");
        tree.WriteCatalog();
        AssertFailure(tree.Root, "Shared");
    }

    [Fact]
    public void Execute_RejectsUnsafeCatalogModuleIdBeforeResolvingPaths()
    {
        using var tree = new Tree();
        File.WriteAllText(Path.Combine(tree.Root, "modules", EngineModuleCatalog.FileName), "Shared\t../outside\n", new UTF8Encoding(false));
        AssertFailure(tree.Root, "Shared");
    }

    [Fact]
    public void Execute_RejectsLinkedModuleDirectoryWhenSymbolicLinksAreAvailable()
    {
        using var tree = new Tree();
        tree.AddMissing("Shared");
        tree.WriteCatalog();
        string module = Path.Combine(tree.Root, "modules", "Shared");
        string target = Path.Combine(tree.Root, "target");
        Directory.CreateDirectory(target);
        File.Copy(typeof(EngineModuleCatalog).Assembly.Location, Path.Combine(target, "Shared.dll"));
        try { Directory.CreateSymbolicLink(module, target); }
        catch (PlatformNotSupportedException exception) { throw SkipException.ForSkip(exception.Message); }
        catch (UnauthorizedAccessException exception) when (OperatingSystem.IsWindows() && exception.HResult == unchecked((int)0x80070522)) { throw SkipException.ForSkip(exception.Message); }
        catch (IOException exception) when (OperatingSystem.IsWindows() && exception.HResult == unchecked((int)0x80070522)) { throw SkipException.ForSkip(exception.Message); }
        AssertFailure(tree.Root, "Shared");
    }

    private static void AssertFailure(string root, string side)
    {
        var engine = new Engine();
        var task = new ResolveKarpikStaticReferencesTask { BuildEngine = engine, EngineRoot = root, Side = side };
        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
        Assert.Empty(task.References);
    }

    private static string Build(Tree tree, string projectName, string name, string version)
    {
        string root = Path.Combine(tree.Root, projectName); Directory.CreateDirectory(root);
        string project = Path.Combine(root, projectName + ".csproj");
        File.WriteAllText(project, $$"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>{{name}}</AssemblyName><AssemblyVersion>{{version}}</AssemblyVersion></PropertyGroup></Project>""");
        File.WriteAllText(Path.Combine(root, "C.cs"), "public class C {}\n");
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("build"); info.ArgumentList.Add(project); info.ArgumentList.Add("-m:1"); info.ArgumentList.Add("-nr:false"); info.ArgumentList.Add("--nologo");
        using var process = Process.Start(info)!; string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.True(process.ExitCode == 0, output);
        return Path.Combine(root, "bin", "Debug", "net10.0", name + ".dll");
    }

    private sealed class Tree : IDisposable
    {
        private readonly List<EngineModuleCatalogEntry> _entries = [];
        public Tree() { Root = Path.Combine(Path.GetTempPath(), "KarpikStaticTask", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(Root, "modules")); }
        public string Root { get; }
        public void Add(string id, string assembly, EngineModuleSide side = EngineModuleSide.Shared) { _entries.Add(new(id, side)); string dir = Path.Combine(Root, "modules", id); Directory.CreateDirectory(dir); File.Copy(assembly, Path.Combine(dir, id + ".dll")); }
        public void AddMissing(string id, EngineModuleSide side = EngineModuleSide.Shared) => _entries.Add(new(id, side));
        public void WriteCatalog() => File.WriteAllText(Path.Combine(Root, "modules", EngineModuleCatalog.FileName), EngineModuleCatalog.Serialize(_entries), new UTF8Encoding(false));
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Engine : IBuildEngine
    { public List<BuildErrorEventArgs> Errors { get; }=[]; public bool ContinueOnError=>false; public int LineNumberOfTaskNode=>0; public int ColumnNumberOfTaskNode=>0; public string ProjectFileOfTaskNode=>""; public void LogErrorEvent(BuildErrorEventArgs e)=>Errors.Add(e); public void LogWarningEvent(BuildWarningEventArgs e){} public void LogMessageEvent(BuildMessageEventArgs e){} public void LogCustomEvent(CustomBuildEventArgs e){} public bool BuildProjectFile(string a,string[] b,IDictionary c,IDictionary d)=>true; }
}

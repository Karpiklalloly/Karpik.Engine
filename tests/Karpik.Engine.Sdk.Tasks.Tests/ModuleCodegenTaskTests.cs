using Karpik.Engine.Sdk.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections;
using System.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace Karpik.Engine.Sdk.Tasks.Tests;

public sealed class ModuleCodegenTaskTests
{
    [Theory]
    [InlineData("Static")]
    [InlineData("Dynamic")]
    public async System.Threading.Tasks.Task SdkGeneratesInDirectAndTransitiveConsumers(string mode)
    {
        using var tree = new CodegenTree();
        string Escape(string value) => System.Security.SecurityElement.Escape(value)!;
        string WriteProject(string name, string body, string source)
        {
            string directory = Path.Combine(tree.Root, name);
            Directory.CreateDirectory(directory);
            string project = Path.Combine(directory, name + ".csproj");
            File.WriteAllText(project, body);
            File.WriteAllText(Path.Combine(directory, "Code.cs"), source);
            return project;
        }
        async System.Threading.Tasks.Task Build(string project)
        {
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { "build", project, "-m:1", "-nr:false", "--nologo" }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        WriteProject("Codegen.Support", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            "public static class CodegenSupport { public static string Source() => \"internal static class Marker { public const int Value = 42; }\"; }");
        string generator = WriteProject("Provider.Codegen", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <Reference Include="Microsoft.CodeAnalysis" HintPath="$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll" Private="false" />
                <ProjectReference Include="../Codegen.Support/Codegen.Support.csproj" />
              </ItemGroup>
            </Project>
            """, """
            using Microsoft.CodeAnalysis;
            [Generator] public sealed class Generator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) => context.AddSource("Marker.g.cs", CodegenSupport.Source());
            }
            """);
        await Build(generator);
        tree.Add("Provider.Codegen");
        foreach (string assembly in Directory.EnumerateFiles(Path.Combine(tree.Root, "Provider.Codegen", "bin", "Debug", "net10.0"), "*.dll"))
            File.Copy(assembly, Path.Combine(tree.Root, "codegen", "Provider.Codegen", Path.GetFileName(assembly)), true);
        tree.WriteCatalog("""
            {"Version":1,"Generators":[{"OwnerAssembly":"Provider","ModuleId":"Provider","AssemblyPath":"codegen/Provider.Codegen/Provider.Codegen.dll"}]}
            """);
        WriteProject("Provider", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", "public class Provider { }");
        foreach (var (name, dependency) in new[] { ("Middle", "Provider"), ("Game", "Middle") })
        {
            WriteProject(name, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <KarpikProjectKind>Test</KarpikProjectKind>
                    <KarpikCompositionMode>{{mode}}</KarpikCompositionMode>
                    <KarpikEngineRoot>{{Escape(tree.Root)}}</KarpikEngineRoot>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../{{dependency}}/{{dependency}}.csproj" /></ItemGroup>
                  <UsingTask TaskName="Karpik.Engine.Sdk.Tasks.ResolveKarpikCodegenTask" AssemblyFile="{{Escape(typeof(ResolveKarpikCodegenTask).Assembly.Location)}}" />
                  <Import Project="{{Escape(Path.Combine(AppContext.BaseDirectory, "Sdk.targets"))}}" />
                  <Target Name="ValidateKarpikProjectReferencesBeforeRestore" />
                  <Target Name="ValidateKarpikProjectReferencesBeforeBuild" />
                </Project>
                """, $"public static class {name} {{ public static int Value => Marker.Value; }}");
        }
        for (int build = 0; build < 2; build++)
            await Build(Path.Combine(tree.Root, "Game", "Game.csproj"));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(tree.Root, "Game", "bin"), "*.dll", SearchOption.AllDirectories),
            file => Path.GetFileName(file).Contains("Codegen", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvesOnlyReferencedOwnersAndSelectedModules(bool selected)
    {
        using var tree = new CodegenTree();
        tree.WriteCatalog("""
            {"Version":1,"Generators":[
              {"OwnerAssembly":"Provider","ModuleId":"Provider","AssemblyPath":"codegen/Provider.Codegen/Provider.Codegen.dll"},
              {"OwnerAssembly":"Core","AssemblyPath":"codegen/Core.Codegen/Core.Codegen.dll"}]}
            """);
        tree.Add("Provider.Codegen");
        tree.Add("Core.Codegen");
        File.Copy(typeof(ModuleCodegenTaskTests).Assembly.Location,
            Path.Combine(tree.Root, "codegen", "Provider.Codegen", "Codegen.Support.dll"));
        var task = new ResolveKarpikCodegenTask
        {
            BuildEngine = new Engine(), EngineRoot = tree.Root,
            ReferencedAssemblies = [new TaskItem("Provider.dll"), new TaskItem("Provider.dll")],
            SelectedModuleIds = selected ? [new TaskItem("Provider")] : []
        };
        Assert.True(task.Execute());
        Assert.Equal(selected ? 2 : 0, task.Analyzers.Length);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Generators\":[null]}")]
    [InlineData("{\"Version\":99,\"Generators\":[]}")]
    [InlineData("{\"Version\":1,\"Generators\":[{\"OwnerAssembly\":\"Provider\",\"AssemblyPath\":\"../outside.dll\"}]}")]
    [InlineData("{\"Version\":1,\"Generators\":[{\"OwnerAssembly\":\"Provider\",\"AssemblyPath\":\"codegen/missing.dll\"}]}")]
    public void RejectsInvalidCatalogs(string catalog)
    {
        using var tree = new CodegenTree();
        tree.WriteCatalog(catalog);
        var engine = new Engine();
        var task = new ResolveKarpikCodegenTask
        {
            BuildEngine = engine, EngineRoot = tree.Root,
            ReferencedAssemblies = [new TaskItem("Provider.dll")]
        };
        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
        Assert.Empty(task.Analyzers);
    }

    [Fact]
    public void LegacyPayloadWithoutCatalogHasNoModuleGenerators()
    {
        using var tree = new CodegenTree();
        var task = new ResolveKarpikCodegenTask { BuildEngine = new Engine(), EngineRoot = tree.Root };
        Assert.True(task.Execute());
        Assert.Empty(task.Analyzers);
    }

    [Fact]
    public void RejectsLinkedDependencyWhenSymbolicLinksAreAvailable()
    {
        using var tree = new CodegenTree();
        tree.Add("Provider.Codegen");
        tree.WriteCatalog("""
            {"Version":1,"Generators":[{"OwnerAssembly":"Provider","AssemblyPath":"codegen/Provider.Codegen/Provider.Codegen.dll"}]}
            """);
        string dependency = Path.Combine(tree.Root, "codegen", "Provider.Codegen", "Codegen.Support.dll");
        try { File.CreateSymbolicLink(dependency, typeof(ModuleCodegenTaskTests).Assembly.Location); }
        catch (PlatformNotSupportedException exception) { throw SkipException.ForSkip(exception.Message); }
        catch (UnauthorizedAccessException exception) when (OperatingSystem.IsWindows() && exception.HResult == unchecked((int)0x80070522)) { throw SkipException.ForSkip(exception.Message); }
        catch (IOException exception) when (OperatingSystem.IsWindows() && exception.HResult == unchecked((int)0x80070522)) { throw SkipException.ForSkip(exception.Message); }
        var engine = new Engine();
        var task = new ResolveKarpikCodegenTask
        {
            BuildEngine = engine, EngineRoot = tree.Root,
            ReferencedAssemblies = [new TaskItem("Provider.dll")]
        };
        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
        Assert.Empty(task.Analyzers);
    }

    [Fact]
    public void SdkFallbackResolvesReferencedOwnerWithoutEngineRoot()
    {
        using var tree = new CodegenTree();
        tree.Add("Provider.Codegen");
        tree.WriteCatalog("""
            {"Version":1,"Generators":[{"OwnerAssembly":"Provider","ModuleId":"Provider","AssemblyPath":"codegen/Provider.Codegen/Provider.Codegen.dll"}]}
            """);
        var task = new ResolveKarpikCodegenTask
        {
            BuildEngine = new Engine(), SdkRoot = tree.Root, RespectModuleSelection = false,
            ReferencedAssemblies = [new TaskItem("Provider.dll")]
        };
        Assert.True(task.Execute());
        Assert.Single(task.Analyzers);
    }

    private sealed class CodegenTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikCodegen", Guid.NewGuid().ToString("N"));
        public CodegenTree() => Directory.CreateDirectory(Path.Combine(Root, "codegen"));
        public void WriteCatalog(string catalog) => File.WriteAllText(Path.Combine(Root, "codegen", "catalog.json"), catalog);
        public void Add(string name)
        {
            string directory = Path.Combine(Root, "codegen", name);
            Directory.CreateDirectory(directory);
            File.Copy(typeof(ModuleCodegenTaskTests).Assembly.Location, Path.Combine(directory, name + ".dll"));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class Engine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string p, string[] t, IDictionary g, IDictionary o) => false;
    }
}

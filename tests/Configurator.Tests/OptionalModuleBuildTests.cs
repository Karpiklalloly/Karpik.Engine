using System.Diagnostics;
using ProjectConfigurator;
using Xunit;

public sealed class OptionalModuleBuildTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModuleCodegenBuildsAndGeneratesInTransitiveConsumer(bool typedReference)
    {
        using var repository = new TestRepository();
        repository.AddPlugin(ProjectSide.Shared, "Provider.Core");
        string generator = repository.AddLibrary("Provider.Core.Codegen");
        repository.AddPlugin(ProjectSide.Shared, "Middle", dependencyIds: [("Provider", false)]);
        string consumer = repository.AddPlugin(ProjectSide.Shared, "Consumer.App", directReference: typedReference ? generator : null,
            dependencyIds: [("Middle", false)]);
        repository.Select("Provider");
        repository.Select("Middle");
        repository.Select("Consumer.App");
        var model = repository.Load();
        var graph = GraphValidator.Validate(model);
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
        ArtifactGenerator.WriteArtifacts(ArtifactGenerator.BuildArtifacts(model, graph));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Repository.Directory.Build.targets"),
            Path.Combine(repository.RootPath, "Directory.Build.targets"));
        var generatorPath = Path.Combine(repository.RootPath, generator);
        File.WriteAllText(generatorPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <Reference Include="Microsoft.CodeAnalysis" HintPath="$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll" Private="false" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(generatorPath)!, "Generator.cs"), """
            using Microsoft.CodeAnalysis;
            [Generator]
            public sealed class MarkerGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) =>
                    context.AddSource("Marker.g.cs", "internal static class Marker { public const int Value = 42; }");
            }
            """);
        File.WriteAllText(Path.Combine(repository.RootPath, Path.GetDirectoryName(consumer)!, "Consumer.cs"),
            "public static class Consumer { public static int Value => Marker.Value; " +
            (typedReference ? "public static object Create() => new MarkerGenerator();" : "") + " }");
        for (int build = 0; build < 2; build++)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repository.RootPath, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { "build", consumer, "-m:1", "-nr:false", "--nologo" }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        Assert.Equal(typedReference, File.Exists(Path.Combine(repository.RootPath, Path.GetDirectoryName(consumer)!,
            "bin/Debug/net10.0/Provider.Core.Codegen.dll")));
    }

    [Theory]
    [InlineData(false, "Provider")]
    [InlineData(false, "Provider.Core")]
    [InlineData(true, "Provider")]
    [InlineData(true, "Provider.Core")]
    public async Task OptionalReferenceBuildsProviderOnlyWhenSelected(bool enabled, string dependencyId)
    {
        using var repository = new TestRepository();
        string provider = repository.AddPlugin(ProjectSide.Shared, "Provider.Core");
        string consumer = repository.AddPlugin(ProjectSide.Shared, "Consumer", dependencyIds: [(dependencyId, true)]);
        repository.Select("Provider", enabled);
        repository.Select("Consumer");
        var model = repository.Load();
        var graph = GraphValidator.Validate(model);
        Assert.True(graph.IsValid, string.Join("\n", graph.Errors));
        ArtifactGenerator.WriteArtifacts(ArtifactGenerator.BuildArtifacts(model, graph));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Repository.Directory.Build.targets"),
            Path.Combine(repository.RootPath, "Directory.Build.targets"));
        File.WriteAllText(Path.Combine(repository.RootPath, Path.GetDirectoryName(provider)!, "Provider.cs"),
            enabled ? "public sealed class ProviderMarker {}" : "#error Optional provider must not be compiled when disabled.");
        File.WriteAllText(Path.Combine(repository.RootPath, Path.GetDirectoryName(consumer)!, "Consumer.cs"), """
            public sealed class Consumer
            {
            #if KARPIK_MODULE_PROVIDER_CORE
                public ProviderMarker Provider;
            #endif
            }
            """);
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository.RootPath, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "build", consumer, "-m:1", "-nr:false", "--nologo" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
        Assert.Equal(enabled, File.Exists(Path.Combine(repository.RootPath, Path.GetDirectoryName(provider)!, "bin/Debug/net10.0/Provider.Core.dll")));
    }
}

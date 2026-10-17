using System.Diagnostics;
using ProjectConfigurator;
using Xunit;

public sealed class OptionalModuleBuildTests
{
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

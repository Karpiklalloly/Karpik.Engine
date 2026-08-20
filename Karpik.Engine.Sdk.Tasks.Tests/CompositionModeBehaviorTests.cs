using System.Diagnostics;
using System.Security;
using Xunit;

public sealed class CompositionModeBehaviorTests
{
    [Theory]
    [InlineData(null, "Dynamic")]
    [InlineData("", "Dynamic")]
    [InlineData("Dynamic", "Dynamic")]
    [InlineData("Static", "Static")]
    public async Task SdkConsumerNormalizesAndExposesAcceptedCompositionModes(string? requestedMode, string expectedMode)
    {
        using var fixture = new CompositionModeFixture(requestedMode);

        BuildResult result = await fixture.BuildAsync();

        Assert.True(result.Succeeded, result.Output);
        string editorConfig = File.ReadAllText(fixture.EditorConfigPath);
        Assert.Contains("build_property.KarpikSide = Server", editorConfig, StringComparison.Ordinal);
        Assert.Contains("build_property.KarpikProjectKind = Test", editorConfig, StringComparison.Ordinal);
        Assert.Contains($"build_property.KarpikCompositionMode = {expectedMode}", editorConfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SdkConsumerRejectsInvalidCompositionModeWithStableKarpik010()
    {
        using var fixture = new CompositionModeFixture("invalid");

        BuildResult result = await fixture.BuildAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("error KARPIK010", result.Output, StringComparison.Ordinal);
        Assert.Contains("KarpikCompositionMode must be exactly 'Dynamic' or 'Static'", result.Output, StringComparison.Ordinal);
    }

    private sealed class CompositionModeFixture : IDisposable
    {
        private readonly string _rootPath;

        public CompositionModeFixture(string? requestedMode)
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "KarpikCompositionModeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);

            string propsPath = Path.Combine(AppContext.BaseDirectory, "Sdk.props");
            string targetsPath = Path.Combine(AppContext.BaseDirectory, "Sdk.targets");
            string modeProperty = requestedMode is null
                ? string.Empty
                : $"    <KarpikCompositionMode>{SecurityElement.Escape(requestedMode)}</KarpikCompositionMode>{Environment.NewLine}";

            ProjectPath = Path.Combine(_rootPath, "CompositionFixture.csproj");
            File.WriteAllText(
                ProjectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{{SecurityElement.Escape(propsPath)}}" />
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <KarpikSide>Server</KarpikSide>
                    <KarpikProjectKind>Test</KarpikProjectKind>
                    <DesignTimeBuild>true</DesignTimeBuild>
                {{modeProperty}}  </PropertyGroup>
                  <Import Project="{{SecurityElement.Escape(targetsPath)}}" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(_rootPath, "Program.cs"), "public static class Program { public static void Main() { } }");
        }

        public string ProjectPath { get; }

        public string EditorConfigPath => Path.Combine(
            _rootPath,
            "obj",
            "Debug",
            "net10.0",
            "CompositionFixture.GeneratedMSBuildEditorConfig.editorconfig");

        public async Task<BuildResult> BuildAsync()
        {
            var startInfo = new ProcessStartInfo("dotnet", $"build \"{ProjectPath}\" -m:1 -nr:false")
            {
                WorkingDirectory = _rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using Process process = Process.Start(startInfo)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new BuildResult(process.ExitCode == 0, output + error);
        }

        public void Dispose()
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed record BuildResult(bool Succeeded, string Output);
}

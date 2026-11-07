using System.Xml.Linq;
using Xunit;

namespace Karpik.Engine.Sdk.IntegrationTests;

public sealed class StaticLauncherNativeStagingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("", "linux-x64", "linux-x64")]
    [InlineData("linux-x64", "win-x64", "linux-x64")]
    [InlineData("linux-arm64", "linux-x64", "linux-arm64")]
    [InlineData("win-x64", "linux-x64", "win-x64")]
    [InlineData("win-arm64", "linux-x64", "win-arm64")]
    [InlineData("osx-arm64", "linux-x64", "osx-arm64")]
    [InlineData("osx-x64", "linux-x64", "osx-x64")]
    public async Task Build_and_publish_stage_only_the_selected_runtime_natives(
        string runtimeIdentifier, string hostIdentifier, string expectedIdentifier)
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikNativeStaging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            XElement staging = XDocument.Load(Path.Combine(repository, "src", "Karpik.Engine.Sdk", "Sdk", "Sdk.targets"))
                .Root!.Elements("Target").Single(target => (string?)target.Attribute("Name") == "_KarpikStageStaticLauncherNatives");
            string engine = Path.Combine(root, "engine");
            void WriteNative(string relative, string value)
            {
                string path = Path.Combine(engine, "native", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, value);
            }

            WriteNative("legacy.dll", "win");
            WriteNative("liblegacy.so", "linux");
            WriteNative("liblegacy.dylib", "osx");
            foreach (string rid in new[] { "linux-x64", "linux-arm64", "win-x64", "win-arm64", "osx-arm64", "osx-x64" })
            {
                string extension = rid.StartsWith("win-", StringComparison.Ordinal) ? ".dll"
                    : rid.StartsWith("osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
                if (rid != "osx-x64")
                    WriteNative(Path.Combine("runtimes", rid, "native", "runtime" + extension), rid);
                WriteNative(Path.Combine(rid, "flat" + extension), rid);
            }
            WriteNative(Path.Combine("runtimes", "osx", "native", "universal.dylib"), "osx");
            WriteNative(Path.Combine("runtimes", "osx", "native", "runtime.dylib"), "osx-x64");

            string buildOutput = Path.Combine(root, "build") + Path.DirectorySeparatorChar;
            string publishOutput = Path.Combine(root, "publish") + Path.DirectorySeparatorChar;
            string project = Path.Combine(root, "staging.proj");
            new XDocument(new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("KarpikCompositionMode", "Static"),
                    new XElement("KarpikProjectKind", "Tool"),
                    new XElement("KarpikEngineRoot", engine),
                    new XElement("RuntimeIdentifier", runtimeIdentifier),
                    new XElement("NETCoreSdkPortableRuntimeIdentifier", hostIdentifier),
                    new XElement("TargetDir", buildOutput)),
                new XElement(staging),
                new XElement("Target", new XAttribute("Name", "Probe"),
                    new XAttribute("DependsOnTargets", "_KarpikStageStaticLauncherNatives"),
                    new XElement("Copy", new XAttribute("SourceFiles", "@(ContentWithTargetPath)"),
                        new XAttribute("DestinationFiles", $"@(ContentWithTargetPath->'{publishOutput}%(TargetPath)')")))))
                .Save(project);

            var harness = new ExternalGameCliTests(output);
            var result = await harness.RunAsync(root, ["msbuild", project, "-t:Probe", "-m:1", "-nr:false"],
                new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" });
            Assert.True(result.ExitCode == 0, result.CombinedOutput);
            string platform = expectedIdentifier.Split('-')[0];
            string expectedExtension = platform == "win" ? ".dll" : platform == "osx" ? ".dylib" : ".so";
            foreach (string directory in new[] { buildOutput, publishOutput })
            {
                Assert.Equal(expectedIdentifier, File.ReadAllText(Path.Combine(directory, "runtime" + expectedExtension)));
                Assert.Equal(expectedIdentifier, File.ReadAllText(Path.Combine(directory, "flat" + expectedExtension)));
                string legacy = platform == "win" ? "legacy.dll" : "liblegacy" + expectedExtension;
                Assert.Equal(platform, File.ReadAllText(Path.Combine(directory, legacy)));
                Assert.Equal(expectedIdentifier == "osx-x64" ? 4 : 3, Directory.GetFiles(directory).Length);
                if (expectedIdentifier == "osx-x64")
                    Assert.Equal("osx", File.ReadAllText(Path.Combine(directory, "universal.dylib")));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

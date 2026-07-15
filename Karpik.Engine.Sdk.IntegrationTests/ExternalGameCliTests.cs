using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Sdk.IntegrationTests;

public sealed class ExternalGameCliTests
{
    private const string PackageVersion = "0.6.0-local";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(15);
    private readonly ITestOutputHelper _output;
    private readonly List<StartedProcess> _startedProcesses = [];

    public ExternalGameCliTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Template_has_the_standard_external_game_structure()
    {
        string templateRoot = GetTemplateRoot();
        string[] expectedFiles =
        [
            ".template.config/template.json",
            "global.json",
            "KarpikGame.slnx",
            "Directory.Solution.targets",
            "Source/KarpikGame.Client/KarpikGame.Client.csproj",
            "Source/KarpikGame.Server/KarpikGame.Server.csproj",
            "Source/KarpikGame.Shared/KarpikGame.Shared.csproj",
            "Tests/KarpikGame.Tests/KarpikGame.Tests.csproj"
        ];

        Assert.All(expectedFiles, relativePath =>
            Assert.True(File.Exists(Path.Combine(templateRoot, Normalize(relativePath))), $"Missing template file: {relativePath}"));

        string[] forbiddenNames = [".karpik", "Directory.Build.props", "Directory.Build.targets"];
        Assert.DoesNotContain(
            Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories),
            path => forbiddenNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(templateRoot, "bin", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(templateRoot, "obj", SearchOption.AllDirectories));
    }

    [Fact]
    public void Template_projects_use_the_karpik_sdk_and_only_legal_static_references()
    {
        string templateRoot = GetTemplateRoot();
        var expected = new Dictionary<string, (string Kind, string Side, string[] References)>(StringComparer.Ordinal)
        {
            ["Source/KarpikGame.Client/KarpikGame.Client.csproj"] =
                ("Runtime", "Client", ["..\\KarpikGame.Shared\\KarpikGame.Shared.csproj"]),
            ["Source/KarpikGame.Server/KarpikGame.Server.csproj"] =
                ("Runtime", "Server", ["..\\KarpikGame.Shared\\KarpikGame.Shared.csproj"]),
            ["Source/KarpikGame.Shared/KarpikGame.Shared.csproj"] =
                ("Runtime", "Shared", []),
            ["Tests/KarpikGame.Tests/KarpikGame.Tests.csproj"] =
                ("Test", "Client", [
                    "..\\..\\Source\\KarpikGame.Client\\KarpikGame.Client.csproj",
                    "..\\..\\Source\\KarpikGame.Shared\\KarpikGame.Shared.csproj"
                ])
        };

        foreach ((string relativePath, (string kind, string side, string[] references)) in expected)
        {
            XDocument document = XDocument.Load(Path.Combine(templateRoot, Normalize(relativePath)));
            XElement root = Assert.IsType<XElement>(document.Root);
            Assert.Equal("Karpik.Engine.Sdk", (string?)root.Attribute("Sdk"));
            Assert.Equal(kind, ReadTopLevelProperty(root, "KarpikProjectKind"));
            Assert.Equal(side, ReadTopLevelProperty(root, "KarpikSide"));

            XElement[] projectReferences = root.Elements("ItemGroup").Elements("ProjectReference").ToArray();
            Assert.All(projectReferences, reference =>
            {
                Assert.NotNull(reference.Attribute("Include"));
                Assert.Null(reference.Attribute("Condition"));
                Assert.Null(reference.Parent?.Attribute("Condition"));
                string include = (string)reference.Attribute("Include")!;
                Assert.DoesNotContain("$(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("@(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("%(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("*", include, StringComparison.Ordinal);
                Assert.DoesNotContain("?", include, StringComparison.Ordinal);
            });
            Assert.Equal(references, projectReferences.Select(reference => (string)reference.Attribute("Include")!).ToArray());
        }
    }

    [Fact]
    public void Template_metadata_pins_the_sdk_and_supports_safe_source_name_replacement()
    {
        string templateRoot = GetTemplateRoot();
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(templateRoot, "global.json")));
        Assert.Equal("10.0.100", globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString());
        Assert.Equal("latestPatch", globalJson.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());
        Assert.Equal(PackageVersion,
            globalJson.RootElement.GetProperty("msbuild-sdks").GetProperty("Karpik.Engine.Sdk").GetString());

        using JsonDocument metadata = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(templateRoot, ".template.config", "template.json")));
        Assert.Equal("KarpikGame", metadata.RootElement.GetProperty("sourceName").GetString());
        Assert.Equal("karpik-game", metadata.RootElement.GetProperty("shortName").GetString());
        Assert.True(metadata.RootElement.GetProperty("preferNameDirectory").GetBoolean());

        string repositoryRoot = GetRepositoryRoot();
        foreach (string path in Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);
            Assert.DoesNotContain(repositoryRoot, content, PathComparison);
            Assert.DoesNotContain("KarpikEngine.csproj", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..\\..\\KarpikEngine", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task External_template_supports_ordinary_cli_workflow_and_validation_precedence()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 to run the external SDK subprocess suite.");

        _startedProcesses.Clear();

        string repositoryRoot = GetRepositoryRoot();
        string templateRoot = GetTemplateRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikExternalSdk_{Guid.NewGuid():N}");
        Assert.False(IsWithinRoot(temporaryRoot, repositoryRoot),
            $"External integration root must be outside the engine repository: {temporaryRoot}");
        Directory.CreateDirectory(temporaryRoot);
        bool cleanupAllowed = true;

        try
        {
            string engineRoot = ResolveValidatedEngineRoot(repositoryRoot);
            string packageFeed = Path.Combine(repositoryRoot, "artifacts", "nuget");
            Directory.CreateDirectory(packageFeed);
            string offlinePackageFeed = Path.Combine(temporaryRoot, "offline-packages");
            SeedOfflinePackageFeed(repositoryRoot, offlinePackageFeed);
            string hive = Path.Combine(temporaryRoot, "template-hive");
            string dotnetHome = Path.Combine(temporaryRoot, "dotnet-home");
            string nugetPackages = Path.Combine(temporaryRoot, "nuget-packages");
            string nugetHttpCache = Path.Combine(temporaryRoot, "nuget-http-cache");
            string nugetConfig = Path.Combine(temporaryRoot, "NuGet.Config");
            WriteNuGetConfig(temporaryRoot, packageFeed, offlinePackageFeed);
            var commonEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DOTNET_CLI_HOME"] = dotnetHome,
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["NUGET_PACKAGES"] = nugetPackages,
                ["NUGET_HTTP_CACHE_PATH"] = nugetHttpCache,
                ["NuGetAudit"] = "false",
                ["RestoreDisableParallel"] = "true",
                ["KarpikEngineRoot"] = engineRoot
            };

            ProcessResult pack = await RunAsync(
                repositoryRoot,
                ["pack", "Karpik.Engine.Sdk\\Karpik.Engine.Sdk.csproj", "-m:1", "-nr:false",
                    $"-p:PackageVersion={PackageVersion}", $"-p:RestoreConfigFile={nugetConfig}", "-o", packageFeed],
                commonEnvironment);
            AssertSuccess(pack, "pack the current Karpik.Engine.Sdk package");
            Assert.True(File.Exists(Path.Combine(packageFeed, $"Karpik.Engine.Sdk.{PackageVersion}.nupkg")));

            ProcessResult install = await RunAsync(
                temporaryRoot,
                ["new", "--debug:custom-hive", hive, "install", templateRoot],
                commonEnvironment);
            AssertSuccess(install, "install the Karpik game template into an isolated hive");

            string renamedRoot = Path.Combine(temporaryRoot, "renamed", "MilestoneFourGame");
            string repeatedRenamedRoot = Path.Combine(temporaryRoot, "renamed-repeat", "MilestoneFourGame");
            await MaterializeAsync(
                temporaryRoot, hive, renamedRoot, "MilestoneFourGame", commonEnvironment);
            await MaterializeAsync(
                temporaryRoot, hive, repeatedRenamedRoot, "MilestoneFourGame", commonEnvironment);
            AssertRenamedMaterializationsAreDeterministic(
                renamedRoot, repeatedRenamedRoot, "MilestoneFourGame", repositoryRoot);

            string validRoot = Path.Combine(temporaryRoot, "valid", "KarpikGame");
            await MaterializeAsync(temporaryRoot, hive, validRoot, "KarpikGame", commonEnvironment);
            AssertGeneratedSourceTree(validRoot, repositoryRoot);
            WriteNuGetConfig(validRoot, packageFeed, offlinePackageFeed);

            string invalidSdkRoot = Path.Combine(temporaryRoot, "invalid-sdk", "KarpikGame");
            string invalidSideRoot = Path.Combine(temporaryRoot, "invalid-side", "KarpikGame");
            CopyDirectory(validRoot, invalidSdkRoot);
            CopyDirectory(validRoot, invalidSideRoot);

            ProcessResult restore = await RunAsync(
                validRoot,
                ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
                commonEnvironment);
            AssertSuccess(restore, "restore the generated solution");
            ProcessResult build = await RunAsync(
                validRoot,
                ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
                commonEnvironment);
            AssertSuccess(build, "build the generated solution");
            ProcessResult test = await RunAsync(
                validRoot,
                ["test", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-build"],
                commonEnvironment);
            AssertSuccess(test, "test the generated solution");
            ProcessResult publish = await RunAsync(
                validRoot,
                ["publish", "Source\\KarpikGame.Client\\KarpikGame.Client.csproj", "-m:1", "-nr:false", "--no-restore"],
                commonEnvironment);
            AssertSuccess(publish, "publish the generated client");
            AssertOutputsAreExternalAndPortable(validRoot, repositoryRoot);

            await AssertMissingSdkFailsBeforeCompilationAsync(
                invalidSdkRoot, packageFeed, offlinePackageFeed, commonEnvironment);
            await AssertForbiddenSideFailsBeforeCompilationAsync(
                invalidSideRoot, packageFeed, offlinePackageFeed, commonEnvironment);
            AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        catch (ExternalProcessTerminationException)
        {
            cleanupAllowed = false;
            throw;
        }
        finally
        {
            if (cleanupAllowed)
            {
                DeleteOwnedTemporaryRoot(temporaryRoot);
            }
        }
    }

    private async Task MaterializeAsync(
        string workingDirectory,
        string hive,
        string gameRoot,
        string gameName,
        IReadOnlyDictionary<string, string?> environment)
    {
        ProcessResult materialize = await RunAsync(
            workingDirectory,
            ["new", "--debug:custom-hive", hive, "karpik-game", "--name", gameName, "--output", gameRoot],
            environment);
        AssertSuccess(materialize, "materialize the Karpik game template");
        Assert.True(File.Exists(Path.Combine(gameRoot, $"{gameName}.slnx")), materialize.CombinedOutput);
    }

    private static void AssertRenamedMaterializationsAreDeterministic(
        string firstRoot,
        string secondRoot,
        string gameName,
        string repositoryRoot)
    {
        AssertGeneratedSourceTree(firstRoot, repositoryRoot);
        AssertGeneratedSourceTree(secondRoot, repositoryRoot);
        string[] firstFiles = Directory.EnumerateFiles(firstRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(firstRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] secondFiles = Directory.EnumerateFiles(secondRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(secondRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(firstFiles, secondFiles);
        Assert.DoesNotContain(firstFiles, path => path.Contains("KarpikGame", StringComparison.Ordinal));
        Assert.Contains($"{gameName}.slnx", firstFiles);

        foreach (string relativePath in firstFiles)
        {
            string firstPath = Path.Combine(firstRoot, relativePath);
            string secondPath = Path.Combine(secondRoot, relativePath);
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
            Assert.DoesNotContain("KarpikGame", File.ReadAllText(firstPath), StringComparison.Ordinal);
        }
    }

    private async Task AssertMissingSdkFailsBeforeCompilationAsync(
        string gameRoot,
        string packageFeed,
        string offlinePackageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);
        ProcessResult restore = await RunAsync(
            gameRoot,
            ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
            environment);
        AssertSuccess(restore, "restore the valid base for the KARPIK001 mutation");

        string projectPath = Path.Combine(gameRoot, "Source", "KarpikGame.Client", "KarpikGame.Client.csproj");
        File.WriteAllText(projectPath,
            File.ReadAllText(projectPath).Replace("Sdk=\"Karpik.Engine.Sdk\"", "Sdk=\"Microsoft.NET.Sdk\"", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(gameRoot, "Source", "KarpikGame.Client", "CompilerMustNotRun.cs"),
            "#error KARPIK001_COMPILER_MARKER_MUST_NOT_RUN\n");

        ProcessResult result = await RunAsync(
            gameRoot,
            ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(result, "KARPIK001", "KARPIK001_COMPILER_MARKER_MUST_NOT_RUN");
    }

    private async Task AssertForbiddenSideFailsBeforeCompilationAsync(
        string gameRoot,
        string packageFeed,
        string offlinePackageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);
        ProcessResult restore = await RunAsync(
            gameRoot,
            ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
            environment);
        AssertSuccess(restore, "restore the valid base for the KARPIK005 mutation");

        string projectPath = Path.Combine(gameRoot, "Source", "KarpikGame.Client", "KarpikGame.Client.csproj");
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        XElement root = Assert.IsType<XElement>(document.Root);
        XElement itemGroup = root.Elements("ItemGroup").First(group => group.Elements("ProjectReference").Any());
        itemGroup.Add(new XElement("ProjectReference",
            new XAttribute("Include", "..\\KarpikGame.Server\\KarpikGame.Server.csproj")));
        document.Save(projectPath);
        File.WriteAllText(Path.Combine(gameRoot, "Source", "KarpikGame.Client", "CompilerMustNotRun.cs"),
            "#error KARPIK005_COMPILER_MARKER_MUST_NOT_RUN\n");

        ProcessResult solutionResult = await RunAsync(
            gameRoot,
            ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(solutionResult, "KARPIK005", "KARPIK005_COMPILER_MARKER_MUST_NOT_RUN");

        ProcessResult projectResult = await RunAsync(
            gameRoot,
            ["build", "Source\\KarpikGame.Client\\KarpikGame.Client.csproj", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(projectResult, "KARPIK005", "KARPIK005_COMPILER_MARKER_MUST_NOT_RUN");
    }

    private static void AssertDiagnosticBeforeCompilation(ProcessResult result, string diagnostic, string marker)
    {
        Assert.NotEqual(0, result.ExitCode);
        int diagnosticIndex = result.CombinedOutput.IndexOf(diagnostic, StringComparison.Ordinal);
        Assert.True(diagnosticIndex >= 0, result.CombinedOutput);
        Assert.DoesNotContain(marker, result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("error CS", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertGeneratedSourceTree(string gameRoot, string repositoryRoot)
    {
        Assert.False(IsWithinRoot(gameRoot, repositoryRoot));
        string[] forbiddenNames = [".karpik", "Directory.Build.props", "Directory.Build.targets"];
        string[] files = Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories).ToArray();
        Assert.DoesNotContain(files,
            path => forbiddenNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        Assert.All(files, path => Assert.DoesNotContain(repositoryRoot, File.ReadAllText(path), PathComparison));
    }

    private static void AssertOutputsAreExternalAndPortable(string gameRoot, string repositoryRoot)
    {
        string[] outputFiles = Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(gameRoot, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "publish"))
            .ToArray();
        Assert.NotEmpty(outputFiles);
        Assert.All(outputFiles, path => Assert.False(IsWithinRoot(path, repositoryRoot)));

        foreach (string dependencyFile in outputFiles.Where(path =>
                     Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain(repositoryRoot, File.ReadAllText(dependencyFile), PathComparison);
        }
    }

    private static string ResolveValidatedEngineRoot(string repositoryRoot)
    {
        string enginesRoot = Path.Combine(repositoryRoot, "artifacts", "karpik-home-final", "Engines");
        Assert.True(Directory.Exists(enginesRoot), $"Retained Milestone 3 engine store is missing: {enginesRoot}");
        string[] candidates = Directory.EnumerateDirectories(enginesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, ".complete")))
            .Where(directory =>
            {
                var result = new EngineInstallationValidator().Validate(directory, PackageVersion);
                return result.IsValid;
            })
            .ToArray();
        string engineRoot = Assert.Single(candidates);
        return Path.GetFullPath(engineRoot);
    }

    private static void SeedOfflinePackageFeed(string repositoryRoot, string destination)
    {
        Directory.CreateDirectory(destination);
        string[] assetsPaths =
        [
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.IntegrationTests", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.Tasks", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk", "obj", "project.assets.json")
        ];
        foreach (string assetsPath in assetsPaths)
        {
            SeedOfflinePackageFeedFromAssets(assetsPath, destination);
        }
        Assert.NotEmpty(Directory.EnumerateFiles(destination, "*.nupkg", SearchOption.TopDirectoryOnly));
    }

    private static void SeedOfflinePackageFeedFromAssets(string assetsPath, string destination)
    {
        Assert.True(File.Exists(assetsPath), $"Project assets are missing: {assetsPath}");

        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        string[] packageFolders = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(folder => Path.GetFullPath(folder.Name))
            .ToArray();
        Assert.NotEmpty(packageFolders);
        foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (!string.Equals(
                    library.Value.GetProperty("type").GetString(),
                    "package",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] identity = library.Name.Split('/', 2);
            Assert.Equal(2, identity.Length);
            string? package = packageFolders
                .Select(folder => Path.Combine(
                    folder,
                    identity[0].ToLowerInvariant(),
                    identity[1].ToLowerInvariant()))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(
                    directory,
                    "*.nupkg",
                    SearchOption.TopDirectoryOnly))
                .FirstOrDefault();
            Assert.True(package is not null,
                $"Resolved package {library.Name} has no .nupkg in project.assets.json packageFolders: " +
                string.Join(", ", packageFolders));
            File.Copy(package, Path.Combine(destination, Path.GetFileName(package)), overwrite: true);
        }
    }

    private static void WriteNuGetConfig(string gameRoot, string packageFeed, string offlinePackageFeed)
    {
        string escapedFeed = System.Security.SecurityElement.Escape(Path.GetFullPath(packageFeed))!;
        string escapedOfflinePackageFeed =
            System.Security.SecurityElement.Escape(Path.GetFullPath(offlinePackageFeed))!;
        string content = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value=".packages" />
              </config>
              <packageSources>
                <clear />
                <add key="Karpik local" value="{{escapedFeed}}" />
                <add key="Offline package cache" value="{{escapedOfflinePackageFeed}}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="Karpik local">
                  <package pattern="Karpik.Engine.Sdk" />
                </packageSource>
                <packageSource key="Offline package cache">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;
        File.WriteAllText(Path.Combine(gameRoot, "NuGet.Config"), content, new UTF8Encoding(false));
    }

    private async Task<ProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach ((string key, string? value) in environment)
        {
            startInfo.Environment[key] = value;
        }
        _startedProcesses.Add(new StartedProcess(
            arguments.ToArray(),
            new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet.");
        }
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        try
        {
            Task exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(CommandTimeout)) != exit)
            {
                throw new TimeoutException($"dotnet {string.Join(' ', arguments)} exceeded {CommandTimeout}.");
            }
            await exit;
        }
        finally
        {
            await ExternalProcessTermination.EnsureStoppedAsync(
                new SystemExternalChildProcess(process),
                TerminationTimeout);
        }

        Task allOutput = Task.WhenAll(standardOutput, standardError);
        if (await Task.WhenAny(allOutput, Task.Delay(TerminationTimeout)) != allOutput)
        {
            throw ExternalProcessTermination.PreservationFailure(
                process.Id,
                $"Output pipes did not close within {TerminationTimeout} after process exit.",
                new TimeoutException("Redirected output drain did not complete after process exit."));
        }
        try
        {
            await allOutput;
        }
        catch (Exception exception) when (!ExternalProcessTermination.IsFatal(exception))
        {
            throw ExternalProcessTermination.PreservationFailure(
                process.Id,
                "Redirected output drain failed after process exit.",
                exception);
        }
        string output = standardOutput.Result;
        string error = standardError.Result;
        var result = new ProcessResult(process.ExitCode, output, error);
        _output.WriteLine($"> dotnet {string.Join(' ', arguments)}\n{result.CombinedOutput}");
        return result;
    }

    private void AssertAllSubprocessesUseOwnedState(string temporaryRoot)
    {
        Assert.NotEmpty(_startedProcesses);
        StartedProcess pack = Assert.Single(
            _startedProcesses,
            process => process.Arguments.FirstOrDefault() == "pack");
        string restoreConfigArgument = Assert.Single(
            pack.Arguments,
            argument => argument.StartsWith("-p:RestoreConfigFile=", StringComparison.OrdinalIgnoreCase));
        string restoreConfig = restoreConfigArgument[(restoreConfigArgument.IndexOf('=') + 1)..];
        Assert.True(IsWithinRoot(restoreConfig, temporaryRoot),
            $"dotnet pack received non-owned RestoreConfigFile: {restoreConfig}");
        Assert.True(File.Exists(restoreConfig), $"dotnet pack RestoreConfigFile is missing: {restoreConfig}");
        foreach (StartedProcess process in _startedProcesses)
        {
            foreach (string key in new[] { "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH" })
            {
                Assert.True(process.Environment.TryGetValue(key, out string? value),
                    $"dotnet {string.Join(' ', process.Arguments)} did not receive {key}.");
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.True(IsWithinRoot(value, temporaryRoot),
                    $"dotnet {string.Join(' ', process.Arguments)} received non-owned {key}: {value}");
            }
        }
    }

    private static void AssertSuccess(ProcessResult result, string operation) =>
        Assert.True(result.ExitCode == 0, $"Failed to {operation}.{Environment.NewLine}{result.CombinedOutput}");

    private static string ReadTopLevelProperty(XElement root, string propertyName) =>
        Assert.Single(root.Elements("PropertyGroup").Elements(propertyName)).Value.Trim();

    private static string GetTemplateRoot() => Path.Combine(GetRepositoryRoot(), "templates", "Karpik.Game");

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsWithinRoot(string candidate, string root)
    {
        string fullCandidate = Path.GetFullPath(candidate);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullCandidate.Equals(fullRoot, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void DeleteOwnedTemporaryRoot(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!IsWithinRoot(fullRoot, tempRoot) || fullRoot.Equals(tempRoot, PathComparison))
        {
            throw new InvalidOperationException($"Refusing to delete a directory outside the temporary root: {fullRoot}");
        }
        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }

    private sealed record StartedProcess(
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment);

}

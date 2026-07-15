using System.Text.Json;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class ManifestAndValidationTests
{
    [Fact]
    public void ManifestUsesTheExactJsonContract()
    {
        var manifest = new EngineInstallationManifest
        {
            EngineVersion = "0.6.0",
            MsBuildSdkVersion = "0.6.0-sdk",
            EditorVersion = "0.6.0",
            RuntimeProtocolVersion = 1,
            LayoutVersion = 1,
            ContentHash = new string('a', 64)
        };

        using JsonDocument document = JsonDocument.Parse(manifest.ToJson());

        Assert.Equal(
            ["engineVersion", "msBuildSdkVersion", "editorVersion", "runtimeProtocolVersion", "layoutVersion", "contentHash"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(manifest.ContentHash, EngineInstallationManifest.Parse(manifest.ToJson()).ContentHash);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"engineVersion\":\"0.6.0\"}")]
    [InlineData("{\"engineVersion\":\"0.6.0\",\"msBuildSdkVersion\":\"sdk\",\"editorVersion\":\"0.6.0\",\"runtimeProtocolVersion\":1,\"layoutVersion\":1,\"contentHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"unknown\":true}")]
    public void ManifestRejectsCorruptMissingAndUnknownFields(string json)
    {
        Assert.ThrowsAny<JsonException>(() => EngineInstallationManifest.Parse(json));
    }

    [Fact]
    public void ContentHashIsDeterministicAcrossCreationOrderAndUsesUnambiguousFraming()
    {
        using var temporary = new TemporaryDirectory();
        string first = Path.Combine(temporary.RootPath, "first");
        string second = Path.Combine(temporary.RootPath, "second");
        string ambiguous = Path.Combine(temporary.RootPath, "ambiguous");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        Directory.CreateDirectory(ambiguous);
        File.WriteAllText(Path.Combine(first, "b"), "two");
        File.WriteAllText(Path.Combine(first, "a"), "one");
        File.WriteAllText(Path.Combine(second, "a"), "one");
        File.WriteAllText(Path.Combine(second, "b"), "two");
        File.WriteAllText(Path.Combine(ambiguous, "ab"), "cone");

        string firstHash = EngineContentHash.Compute(first);

        Assert.Equal(firstHash, EngineContentHash.Compute(second));
        Assert.NotEqual(firstHash, EngineContentHash.Compute(ambiguous));
        File.WriteAllText(Path.Combine(first, "engine-installation.json"), "mutable");
        File.WriteAllText(Path.Combine(first, ".complete"), "mutable");
        Assert.Equal(firstHash, EngineContentHash.Compute(first));
    }

    [Fact]
    public void ValidatorAcceptsACompleteExactInstallation()
    {
        using var temporary = new TemporaryDirectory();
        string root = TestInstallation.Create(temporary.RootPath);

        EngineInstallationValidationResult result = new EngineInstallationValidator()
            .Validate(root, expectedSdkVersion: "0.6.0-sdk", expectedEngineVersion: "0.6.0");

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(EngineInstallationValidationCode.Valid, result.Code);
    }

    [Theory]
    [InlineData("layout", EngineInstallationValidationCode.WrongLayoutVersion)]
    [InlineData("runtime", EngineInstallationValidationCode.WrongRuntimeProtocolVersion)]
    [InlineData("engine", EngineInstallationValidationCode.WrongEngineVersion)]
    [InlineData("sdk", EngineInstallationValidationCode.WrongSdkVersion)]
    [InlineData("editor", EngineInstallationValidationCode.WrongEditorVersion)]
    public void ValidatorRejectsWrongManifestVersions(string mutation, EngineInstallationValidationCode expectedCode)
    {
        using var temporary = new TemporaryDirectory();
        string root = TestInstallation.Create(temporary.RootPath);
        switch (mutation)
        {
            case "layout": TestInstallation.RewriteManifest(root, layoutVersion: 2); break;
            case "runtime": TestInstallation.RewriteManifest(root, runtimeProtocolVersion: 2); break;
            case "engine": TestInstallation.RewriteManifest(root, engineVersion: "9.0.0"); break;
            case "sdk": TestInstallation.RewriteManifest(root, sdkVersion: "9.0.0"); break;
            case "editor": TestInstallation.RewriteManifest(root, editorVersion: "9.0.0"); break;
        }

        EngineInstallationValidationResult result = new EngineInstallationValidator()
            .Validate(root, expectedSdkVersion: "0.6.0-sdk", expectedEngineVersion: "0.6.0");

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, result.Code);
    }

    [Theory]
    [InlineData("runners/client/Karpik.Engine.Core.Runner.dll", EngineInstallationValidationCode.MissingRunner)]
    [InlineData("runners/server/Karpik.Engine.Core.Runner.dll", EngineInstallationValidationCode.MissingRunner)]
    [InlineData(".complete", EngineInstallationValidationCode.MissingCompletionMarker)]
    [InlineData("editor/Karpik.Editor.dll", EngineInstallationValidationCode.MissingEditor)]
    [InlineData("modules/Module/Module.dll", EngineInstallationValidationCode.MissingModules)]
    public void ValidatorRejectsMissingRequiredPayloadEntries(string relativePath, EngineInstallationValidationCode expectedCode)
    {
        using var temporary = new TemporaryDirectory();
        string root = TestInstallation.Create(temporary.RootPath);
        File.Delete(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        EngineInstallationValidationResult result = new EngineInstallationValidator().Validate(root);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void ValidatorRejectsCorruptUnknownManifestAndHashMismatch()
    {
        using var temporary = new TemporaryDirectory();
        var validator = new EngineInstallationValidator();
        string corrupt = TestInstallation.Create(temporary.RootPath, "corrupt");
        File.WriteAllText(Path.Combine(corrupt, "engine-installation.json"), "{bad");
        string unknown = TestInstallation.Create(temporary.RootPath, "unknown");
        string unknownJson = File.ReadAllText(Path.Combine(unknown, "engine-installation.json")).TrimEnd();
        File.WriteAllText(Path.Combine(unknown, "engine-installation.json"), unknownJson[..^1] + ",\"unknown\":true}");
        string mismatch = TestInstallation.Create(temporary.RootPath, "mismatch");
        File.AppendAllText(Path.Combine(mismatch, "modules", "Module", "Module.dll"), "changed");

        Assert.Equal(EngineInstallationValidationCode.CorruptManifest, validator.Validate(corrupt).Code);
        Assert.Equal(EngineInstallationValidationCode.ManifestContractMismatch, validator.Validate(unknown).Code);
        Assert.Equal(EngineInstallationValidationCode.HashMismatch, validator.Validate(mismatch).Code);
    }

    [Fact]
    public void HashingRejectsAReparseEscapeWhenThePlatformAllowsCreatingOne()
    {
        using var temporary = new TemporaryDirectory();
        string root = Path.Combine(temporary.RootPath, "root");
        string outside = Path.Combine(temporary.RootPath, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret"), "outside");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() => EngineContentHash.Compute(root));
    }
}

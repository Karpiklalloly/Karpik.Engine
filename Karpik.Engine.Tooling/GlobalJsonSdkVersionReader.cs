using System.Text.Json;

namespace Karpik.Engine.Tooling;

public enum GlobalJsonSdkVersionCode
{
    Success,
    InvalidPath,
    MissingFile,
    InvalidJson,
    MissingSdkVersion
}

public sealed record GlobalJsonSdkVersionResult(
    bool IsSuccess,
    GlobalJsonSdkVersionCode Code,
    string Message,
    string? SdkVersion = null);

public sealed class GlobalJsonSdkVersionReader
{
    public GlobalJsonSdkVersionResult Read(string globalJsonPath)
    {
        string path;
        try
        {
            path = Path.GetFullPath(globalJsonPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(GlobalJsonSdkVersionCode.InvalidPath, $"Invalid global.json path: {exception.Message}");
        }

        if (!File.Exists(path))
        {
            return Failure(GlobalJsonSdkVersionCode.MissingFile, $"global.json does not exist: {path}");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("msbuild-sdks", out JsonElement sdkMappings) ||
                sdkMappings.ValueKind != JsonValueKind.Object ||
                !sdkMappings.TryGetProperty("Karpik.Engine.Sdk", out JsonElement sdkVersion) ||
                sdkVersion.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sdkVersion.GetString()))
            {
                return Failure(GlobalJsonSdkVersionCode.MissingSdkVersion, "global.json does not define a non-empty msbuild-sdks/Karpik.Engine.Sdk version.");
            }

            return new GlobalJsonSdkVersionResult(true, GlobalJsonSdkVersionCode.Success, "Karpik.Engine.Sdk version found.", sdkVersion.GetString());
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Failure(GlobalJsonSdkVersionCode.InvalidJson, $"Cannot read global.json: {exception.Message}");
        }
    }

    private static GlobalJsonSdkVersionResult Failure(GlobalJsonSdkVersionCode code, string message) =>
        new(false, code, message);
}

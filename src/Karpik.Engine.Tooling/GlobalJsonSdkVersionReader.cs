using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Описывает результат чтения закреплённой SDK-версии из global.json.</summary>
public enum GlobalJsonSdkVersionCode
{
    /// <summary>Версия успешно найдена.</summary>
    Success,
    /// <summary>Путь к global.json некорректен.</summary>
    InvalidPath,
    /// <summary>Файл global.json отсутствует.</summary>
    MissingFile,
    /// <summary>Файл нельзя прочитать как JSON.</summary>
    InvalidJson,
    /// <summary>В файле нет непустой версии Karpik.Engine.Sdk.</summary>
    MissingSdkVersion
}

/// <summary>Содержит результат чтения версии SDK из global.json.</summary>
/// <param name="IsSuccess">Указывает на успешность операции.</param>
/// <param name="Code">Код результата.</param>
/// <param name="Message">Текст для пользователя или диагностики.</param>
/// <param name="SdkVersion">Найденная версия SDK при успехе.</param>
public sealed record GlobalJsonSdkVersionResult(
    bool IsSuccess,
    GlobalJsonSdkVersionCode Code,
    string Message,
    string? SdkVersion = null);

/// <summary>Читает точную версию Karpik.Engine.Sdk из standard global.json.</summary>
public sealed class GlobalJsonSdkVersionReader
{
    /// <summary>Читает закреплённую версию SDK.</summary>
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

    /// <summary>Создаёт неуспешный результат чтения.</summary>
    private static GlobalJsonSdkVersionResult Failure(GlobalJsonSdkVersionCode code, string message) =>
        new(false, code, message);
}

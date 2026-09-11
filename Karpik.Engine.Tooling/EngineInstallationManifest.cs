using System.Text;
using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Описывает совместимость и содержимое установленного engine payload.</summary>
public sealed class EngineInstallationManifest
{
    /// <summary>Текущая версия layout engine payload.</summary>
    public const int CurrentLayoutVersion = 2;
    /// <summary>Текущая версия протокола запуска runtime.</summary>
    public const int CurrentRuntimeProtocolVersion = 1;

    /// <summary>Получает версию движка.</summary>
    public required string EngineVersion { get; init; }
    /// <summary>Получает совместимую точную версию MSBuild SDK.</summary>
    public required string MsBuildSdkVersion { get; init; }
    /// <summary>Получает версию редактора, поставляемого с payload.</summary>
    public required string EditorVersion { get; init; }
    /// <summary>Получает версию runtime-протокола payload.</summary>
    public required int RuntimeProtocolVersion { get; init; }
    /// <summary>Получает версию layout payload.</summary>
    public required int LayoutVersion { get; init; }
    /// <summary>Получает хеш файлов payload, исключая manifest и completion marker.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Сериализует manifest в канонический JSON.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("engineVersion", EngineVersion);
            writer.WriteString("msBuildSdkVersion", MsBuildSdkVersion);
            writer.WriteString("editorVersion", EditorVersion);
            writer.WriteNumber("runtimeProtocolVersion", RuntimeProtocolVersion);
            writer.WriteNumber("layoutVersion", LayoutVersion);
            writer.WriteString("contentHash", ContentHash);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    /// <summary>Разбирает и проверяет manifest установки.</summary>
    public static EngineInstallationManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ManifestContractException("The engine installation manifest root must be an object.");
        }

        string[] expectedNames =
        [
            "engineVersion",
            "msBuildSdkVersion",
            "editorVersion",
            "runtimeProtocolVersion",
            "layoutVersion",
            "contentHash"
        ];
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!expectedNames.Contains(property.Name, StringComparer.Ordinal) ||
                !properties.TryAdd(property.Name, property.Value))
            {
                throw new ManifestContractException($"Unexpected or duplicate manifest field '{property.Name}'.");
            }
        }

        if (properties.Count != expectedNames.Length || expectedNames.Any(name => !properties.ContainsKey(name)))
        {
            throw new ManifestContractException("The engine installation manifest does not contain the exact required field set.");
        }

        return new EngineInstallationManifest
        {
            EngineVersion = ReadString(properties, "engineVersion"),
            MsBuildSdkVersion = ReadString(properties, "msBuildSdkVersion"),
            EditorVersion = ReadString(properties, "editorVersion"),
            RuntimeProtocolVersion = ReadInt32(properties, "runtimeProtocolVersion"),
            LayoutVersion = ReadInt32(properties, "layoutVersion"),
            ContentHash = ReadString(properties, "contentHash")
        };
    }

    /// <summary>Читает обязательное строковое свойство manifest.</summary>
    private static string ReadString(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (properties[name].ValueKind != JsonValueKind.String)
        {
            throw new ManifestContractException($"Manifest field '{name}' must be a string.");
        }

        return properties[name].GetString()!;
    }

    /// <summary>Читает обязательное целочисленное свойство manifest.</summary>
    private static int ReadInt32(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (properties[name].ValueKind != JsonValueKind.Number || !properties[name].TryGetInt32(out int value))
        {
            throw new ManifestContractException($"Manifest field '{name}' must be a 32-bit integer.");
        }

        return value;
    }
}

/// <summary>Представляет нарушение формата installation manifest.</summary>
internal sealed class ManifestContractException(string message) : JsonException(message);

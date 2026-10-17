using System.Text;
using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Описывает сериализуемый запрос launcher'у на открытие решения другим редактором.</summary>
public sealed record EditorHandoffRequest
{
    /// <summary>Текущая версия протокола handoff-запроса.</summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>Создаёт запрос текущей версии для указанного решения.</summary>
    public EditorHandoffRequest(string solutionPath)
        : this(CurrentProtocolVersion, NormalizeSolutionPath(solutionPath))
    {
    }

    /// <summary>Создаёт нормализованный запрос с явно указанной версией протокола.</summary>
    private EditorHandoffRequest(int protocolVersion, string solutionPath)
    {
        ProtocolVersion = protocolVersion;
        SolutionPath = solutionPath;
    }

    /// <summary>Получает версию wire-протокола handoff.</summary>
    public int ProtocolVersion { get; }
    /// <summary>Получает нормализованный абсолютный путь к решению.</summary>
    public string SolutionPath { get; }

    /// <summary>Сериализует запрос в JSON wire-формата.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", ProtocolVersion);
            writer.WriteString("solutionPath", SolutionPath);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    /// <summary>Атомарно записывает запрос в указанный handoff-файл.</summary>
    public void Write(string handoffPath)
    {
        string path = NormalizeHandoffPath(handoffPath);
        string? directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }
        if (IsReparsePoint(directory))
        {
            throw new InvalidDataException($"The handoff directory is a link or reparse point: {directory}");
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(ToJson());
    }

    /// <summary>Читает и разбирает handoff-запрос из файла.</summary>
    public static EditorHandoffRequest Read(string handoffPath)
    {
        string path = NormalizeHandoffPath(handoffPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Editor handoff request was not found.", path);
        }
        if (IsReparsePoint(path))
        {
            throw new InvalidDataException($"The handoff request is a link or reparse point: {path}");
        }
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Разбирает и проверяет JSON handoff-запроса.</summary>
    public static EditorHandoffRequest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The editor handoff request root must be an object.");
            }

            JsonElement? protocolVersion = null;
            JsonElement? solutionPath = null;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "protocolVersion" when protocolVersion is null:
                        protocolVersion = property.Value;
                        break;
                    case "solutionPath" when solutionPath is null:
                        solutionPath = property.Value;
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected or duplicate handoff field '{property.Name}'.");
                }
            }

            if (protocolVersion is null || solutionPath is null ||
                protocolVersion.Value.ValueKind != JsonValueKind.Number ||
                !protocolVersion.Value.TryGetInt32(out int version) ||
                version != CurrentProtocolVersion ||
                solutionPath.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The editor handoff request has an invalid contract or protocol version.");
            }

            return new EditorHandoffRequest(
                version,
                NormalizeSolutionPath(solutionPath.Value.GetString()!));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The editor handoff request is not valid JSON.", exception);
        }
    }

    /// <summary>Нормализует и проверяет путь к решению.</summary>
    private static string NormalizeSolutionPath(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!Path.IsPathFullyQualified(solutionPath))
        {
            throw new InvalidDataException("The handoff solution path must be absolute.");
        }
        string path = Path.GetFullPath(solutionPath);
        if (!string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) ||
            IsReparsePoint(path))
        {
            throw new InvalidDataException("The handoff solution must be an existing, non-linked .slnx file.");
        }
        return path;
    }

    /// <summary>Нормализует и проверяет путь handoff-файла.</summary>
    private static string NormalizeHandoffPath(string handoffPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffPath);
        if (!Path.IsPathFullyQualified(handoffPath))
        {
            throw new ArgumentException("The handoff path must be absolute.", nameof(handoffPath));
        }
        return Path.GetFullPath(handoffPath);
    }

    /// <summary>Определяет, является ли путь ссылкой или reparse point.</summary>
    private static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }
}

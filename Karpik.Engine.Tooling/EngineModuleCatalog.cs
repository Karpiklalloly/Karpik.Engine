using System.Text;

namespace Karpik.Engine.Tooling;

/// <summary>Определяет runtime-сторону модуля в engine payload.</summary>
public enum EngineModuleSide : byte
{
    Shared,
    Client,
    Server
}

/// <summary>Определяет роль модуля в логическом модуле.</summary>
public enum EngineModuleKind : byte
{
    Standalone,
    Core,
    Implementation
}

/// <summary>Описывает зависимость модуля.</summary>
public readonly record struct EngineModuleDependency(string ModuleId, bool Optional);

/// <summary>Описывает одну запись канонического каталога модулей.</summary>
public readonly record struct EngineModuleCatalogEntry
{
    public string ModuleId { get; }
    public string? LogicalModuleId { get; }
    public EngineModuleKind Kind { get; }
    public EngineModuleSide Side { get; }
    public string? ImplementationId { get; }
    public IReadOnlyList<EngineModuleDependency> Dependencies { get; }
    public bool HasSelectionMetadata => LogicalModuleId is not null;

    public EngineModuleCatalogEntry(string moduleId, EngineModuleSide side)
        : this(moduleId, null, default, side, null, [])
    {
    }

    public EngineModuleCatalogEntry(
        string moduleId,
        string? logicalModuleId,
        EngineModuleKind kind,
        EngineModuleSide side,
        string? implementationId,
        IReadOnlyList<EngineModuleDependency> dependencies)
    {
        ModuleId = moduleId;
        LogicalModuleId = logicalModuleId;
        Kind = kind;
        Side = side;
        ImplementationId = implementationId;
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }
}

/// <summary>Читает, проверяет и сериализует канонический каталог модулей payload.</summary>
public static class EngineModuleCatalog
{
    public const string FileName = "modules.catalog";
    public const string ExtendedFormatMarker = "v2";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Serialize(IEnumerable<EngineModuleCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        EngineModuleCatalogEntry[] canonical = Canonicalize(Validate(entries));
        if (canonical.All(entry => !entry.HasSelectionMetadata))
        {
            return string.Concat(canonical.Select(entry => $"{entry.Side}\t{entry.ModuleId}\n"));
        }
        if (canonical.Any(entry => !entry.HasSelectionMetadata))
        {
            throw new InvalidDataException("Engine module catalog cannot mix legacy and selection metadata entries.");
        }

        var builder = new StringBuilder(32 * canonical.Length);
        builder.Append(ExtendedFormatMarker).Append('\n');
        foreach (EngineModuleCatalogEntry entry in canonical)
        {
            string dependencies = string.Join(',', entry.Dependencies
                .OrderBy(dependency => dependency.ModuleId, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.Optional)
                .Select(dependency => dependency.Optional ? "?" + dependency.ModuleId : dependency.ModuleId));
            builder.Append(entry.Side).Append('\t')
                .Append(entry.ModuleId).Append('\t')
                .Append(entry.LogicalModuleId).Append('\t')
                .Append(entry.Kind).Append('\t')
                .Append(entry.ImplementationId ?? string.Empty).Append('\t')
                .Append(dependencies).Append('\n');
        }
        return builder.ToString();
    }

    public static EngineModuleCatalogEntry[] Read(string modulesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        string path = Path.Combine(Path.GetFullPath(modulesRoot), FileName);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Engine module catalog is missing: {path}");
        }
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 1024 * 1024)
        {
            throw new InvalidDataException("Engine module catalog must be non-empty and no larger than 1 MiB.");
        }
        return Parse(File.ReadAllBytes(path));
    }

    public static EngineModuleCatalogEntry[] Parse(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Engine module catalog must be valid UTF-8.", exception);
        }
        if (bytes.StartsWith(Encoding.UTF8.Preamble) || text.Length == 0 || !text.EndsWith('\n') || text.Contains('\r'))
        {
            throw new InvalidDataException("Engine module catalog must use canonical UTF-8 without BOM and LF line endings.");
        }

        string[] lines = text[..^1].Split('\n');
        var entries = new List<EngineModuleCatalogEntry>();
        bool extended = lines[0] == ExtendedFormatMarker;
        int start = extended ? 1 : 0;
        if (extended && lines.Length == 1)
        {
            throw new InvalidDataException("Extended engine module catalog must contain entries.");
        }

        for (int index = start; index < lines.Length; index++)
        {
            string line = lines[index];
            string[] fields = line.Split('\t');
            if (extended)
            {
                if (fields.Length != 6 || !Enum.TryParse(fields[0], false, out EngineModuleSide side) ||
                    !Enum.TryParse(fields[3], false, out EngineModuleKind kind) ||
                    string.IsNullOrWhiteSpace(fields[2]))
                {
                    throw new InvalidDataException($"Invalid extended engine module catalog entry: {line}");
                }

                string? implementation = string.IsNullOrEmpty(fields[4]) ? null : fields[4];
                var dependencies = fields[5].Length == 0
                    ? Array.Empty<EngineModuleDependency>()
                    : fields[5].Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => new EngineModuleDependency(value[0] == '?' ? value[1..] : value, value[0] == '?'))
                        .ToArray();
                entries.Add(new EngineModuleCatalogEntry(fields[1], fields[2], kind, side, implementation, dependencies));
            }
            else
            {
                if (fields.Length != 2 || !Enum.TryParse(fields[0], false, out EngineModuleSide side))
                {
                    throw new InvalidDataException($"Invalid engine module catalog entry: {line}");
                }
                entries.Add(new EngineModuleCatalogEntry(fields[1], side));
            }
        }

        EngineModuleCatalogEntry[] canonical = Canonicalize(Validate(entries));
        if (!bytes.SequenceEqual(StrictUtf8.GetBytes(Serialize(canonical))))
        {
            throw new InvalidDataException("Engine module catalog entries are not canonical or deterministically sorted.");
        }
        return canonical;
    }

    public static EngineModuleCatalogEntry[] ForSide(IEnumerable<EngineModuleCatalogEntry> entries, EngineModuleSide side)
    {
        if (side is EngineModuleSide.Shared)
        {
            throw new ArgumentOutOfRangeException(nameof(side), "A runtime side must be Client or Server.");
        }
        return Canonicalize(Validate(entries)
            .Where(entry => entry.Side is EngineModuleSide.Shared || entry.Side == side));
    }

    private static EngineModuleCatalogEntry[] Canonicalize(IEnumerable<EngineModuleCatalogEntry> entries) => entries
        .OrderBy(entry => entry.Side)
        .ThenBy(entry => entry.ModuleId, StringComparer.Ordinal)
        .ToArray();

    private static EngineModuleCatalogEntry[] Validate(IEnumerable<EngineModuleCatalogEntry> entries)
    {
        EngineModuleCatalogEntry[] materialized = entries.ToArray();
        if (materialized.Length == 0 || materialized.Length > 4096)
        {
            throw new InvalidDataException("Engine module catalog must be non-empty and bounded.");
        }
        var ids = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
        foreach (EngineModuleCatalogEntry entry in materialized)
        {
            if (!Enum.IsDefined(entry.Side) || !ModuleLayoutPolicy.IsSafeModuleId(entry.ModuleId) || !ids.Add(entry.ModuleId))
            {
                throw new InvalidDataException($"Engine module catalog contains an invalid or duplicate module ID: {entry.ModuleId}");
            }
            if (!entry.HasSelectionMetadata)
            {
                continue;
            }
            if (!ModuleLayoutPolicy.IsSafeModuleId(entry.LogicalModuleId) || !Enum.IsDefined(entry.Kind) ||
                (entry.Kind == EngineModuleKind.Implementation) != !string.IsNullOrWhiteSpace(entry.ImplementationId) ||
                (entry.ImplementationId is not null && !ModuleLayoutPolicy.IsSafeModuleId(entry.ImplementationId)))
            {
                throw new InvalidDataException($"Engine module catalog contains invalid selection metadata for: {entry.ModuleId}");
            }
            var dependencies = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
            foreach (EngineModuleDependency dependency in entry.Dependencies)
            {
                if (!ModuleLayoutPolicy.IsSafeModuleId(dependency.ModuleId) || !dependencies.Add(dependency.ModuleId))
                {
                    throw new InvalidDataException($"Engine module catalog contains an invalid or duplicate dependency for: {entry.ModuleId}");
                }
            }
        }
        return materialized;
    }
}

using System.Text;

namespace Karpik.Engine.Tooling;

public enum EngineModuleSide : byte
{
    Shared,
    Client,
    Server
}

public readonly record struct EngineModuleCatalogEntry(string ModuleId, EngineModuleSide Side);

public static class EngineModuleCatalog
{
    public const string FileName = "modules.catalog";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Serialize(IEnumerable<EngineModuleCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        EngineModuleCatalogEntry[] canonical = Validate(entries)
            .OrderBy(entry => entry.Side)
            .ThenBy(entry => entry.ModuleId, StringComparer.Ordinal)
            .ToArray();
        return string.Concat(canonical.Select(entry => $"{entry.Side}\t{entry.ModuleId}\n"));
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

        var entries = new List<EngineModuleCatalogEntry>();
        foreach (string line in text[..^1].Split('\n'))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2 || !Enum.TryParse(fields[0], ignoreCase: false, out EngineModuleSide side))
            {
                throw new InvalidDataException($"Invalid engine module catalog entry: {line}");
            }
            entries.Add(new EngineModuleCatalogEntry(fields[1], side));
        }

        EngineModuleCatalogEntry[] canonical = Validate(entries)
            .OrderBy(entry => entry.Side)
            .ThenBy(entry => entry.ModuleId, StringComparer.Ordinal)
            .ToArray();
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
        return Validate(entries)
            .Where(entry => entry.Side is EngineModuleSide.Shared || entry.Side == side)
            .OrderBy(entry => entry.Side)
            .ThenBy(entry => entry.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }

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
        }
        return materialized;
    }
}

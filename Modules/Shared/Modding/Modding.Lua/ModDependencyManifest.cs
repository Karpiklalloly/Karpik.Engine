using System.Collections.Immutable;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Karpik.Engine.Shared.Modding.Lua;

internal sealed class ModDependencyException(string message) : Exception(message);

internal readonly record struct ModDependencyDeclaration(string Id, bool Optional, Version? MinVersion, Version? MaxVersionExclusive);

internal sealed record ModDependencyManifest(ImmutableArray<ModDependencyDeclaration> Shared,
    ImmutableArray<ModDependencyDeclaration> Client, ImmutableArray<ModDependencyDeclaration> Server)
{
    private static readonly Version FirstNumericVersion = new(0, 0, 0);

    public static ModDependencyManifest Empty { get; } = new([], [], []);

    public static ModDependencyManifest Parse(string modId, JToken? dependencies)
    {
        if (dependencies is null) return Empty;
        if (dependencies is not JObject scopes)
            throw new ModDependencyException($"Mod '{modId}' dependencies must be a JSON object.");

        foreach (var property in scopes.Properties())
            if (property.Name is not ("shared" or "client" or "server"))
                throw new ModDependencyException($"Mod '{modId}' dependencies.{property.Name} is not a recognized scope.");

        return new ModDependencyManifest(ParseGroup(modId, "shared", scopes["shared"]),
            ParseGroup(modId, "client", scopes["client"]), ParseGroup(modId, "server", scopes["server"]));
    }

    private static ImmutableArray<ModDependencyDeclaration> ParseGroup(string modId, string scope, JToken? token)
    {
        if (token is null) return [];
        if (token is not JArray entries)
            throw new ModDependencyException($"Mod '{modId}' dependencies.{scope} must be a JSON array.");
        if (entries.Count == 0) return [];
        var declarations = ImmutableArray.CreateBuilder<ModDependencyDeclaration>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not JObject entry)
                throw new ModDependencyException($"{Context(index)} must be a JSON object.");
            if (entry["id"] is not { Type: JTokenType.String } idToken ||
                string.IsNullOrWhiteSpace((string?)idToken))
                throw new ModDependencyException($"{Context(index)}.id must be a nonblank JSON string.");

            string id = (string)idToken!;
            if (StringComparer.Ordinal.Equals(id, modId))
                throw new ModDependencyException($"{Context(index, id)}: a mod cannot depend on itself.");
            if (!ids.Add(id))
                throw new ModDependencyException($"{Context(index, id)}: duplicate dependency in the same scope.");

            bool optional = false;
            Version? minimum = null, maximum = null;
            foreach (var property in entry.Properties())
            {
                switch (property.Name)
                {
                    case "id":
                        break;
                    case "optional":
                        if (property.Value.Type != JTokenType.Boolean)
                            throw new ModDependencyException($"{Context(index, id)}: optional must be a JSON boolean.");
                        optional = (bool)property.Value;
                        break;
                    case "minVersion":
                        minimum = ReadBound(property, index, id);
                        break;
                    case "maxVersionExclusive":
                        maximum = ReadBound(property, index, id);
                        break;
                    default:
                        throw new ModDependencyException($"{Context(index, id)}: unrecognized field '{property.Name}'.");
                }
            }
            if (maximum is not null && (minimum ?? FirstNumericVersion) >= maximum)
            {
                string lower = minimum is null ? "the implicit minimum 0.0.0" : $"minVersion '{entry["minVersion"]}'";
                throw new ModDependencyException($"{Context(index, id)}: {lower} and maxVersionExclusive '{entry["maxVersionExclusive"]}' form an empty or reversed version interval.");
            }
            declarations.Add(new ModDependencyDeclaration(id, optional, minimum, maximum));
        }
        return declarations.MoveToImmutable();

        string Context(int index, string? target = null) => target is null
            ? $"Mod '{modId}' dependencies.{scope}[{index}]"
            : $"Mod '{modId}' dependencies.{scope}[{index}] target '{target}'";

        Version ReadBound(JProperty property, int index, string target)
        {
            if (property.Value.Type == JTokenType.String && ParseNumericVersion((string?)property.Value) is { } version)
                return version;
            throw new ModDependencyException($"{Context(index, target)}: {property.Name} must be a JSON string in MAJOR.MINOR.PATCH format with nonnegative Int32 components.");
        }
    }

    internal static Version? ParseNumericVersion(string? text)
    {
        ReadOnlySpan<char> span = text.AsSpan();
        Span<Range> parts = stackalloc Range[3];
        if (span.Split(parts, '.') != parts.Length) return null;
        if (!ReadComponent(span[parts[0]], out int major) || !ReadComponent(span[parts[1]], out int minor) ||
            !ReadComponent(span[parts[2]], out int patch)) return null;
        return new Version(major, minor, patch);
    }

    private static bool ReadComponent(ReadOnlySpan<char> span, out int component)
    {
        component = 0;
        return !span.ContainsAnyExceptInRange('0', '9') &&
            int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out component);
    }
}

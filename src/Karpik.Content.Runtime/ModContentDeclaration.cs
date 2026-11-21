using System.Text.Json;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

internal static class ModContentDeclaration
{
    internal static List<ModContentOverride> Parse(byte[]? bytes, string source, string generationId,
        Action<ContentDiagnostic>? report, CancellationToken ct)
    {
        List<ModContentOverride> rules = [];
        if (bytes is null) return rules;
        try
        {
            ReadOnlyMemory<byte> json = bytes;
            if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xef, 0xbb, 0xbf])) json = json[3..];
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetUnique(root, "schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out int version) || version != 1 ||
                !TryGetUnique(root, "generationId", out var generation) || generation.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(generation.GetString(), "N", out var declaredId) || declaredId != Guid.ParseExact(generationId, "N") ||
                !TryGetUnique(root, "overrides", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Expected a v1 override declaration for generation '{generationId}': '{source}'.");

            int index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (ParseRule(entry, out string? targetText) is { } rule) rules.Add(rule);
                else if (report is not null)
                {
                    string label = targetText is { Length: > 128 } ? targetText[..128] : targetText ?? "<invalid>";
                    report(new ContentDiagnostic("KCR301", ContentDiagnosticSeverity.Error, source,
                        $"Invalid override record [{index}] for target '{label}'."));
                }
                index++;
            }
            return rules;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"Invalid Content override declaration: '{source}'.", e);
        }
    }

    private static ModContentOverride? ParseRule(JsonElement entry, out string? targetText)
    {
        targetText = null;
        try
        {
            targetText = GetString(entry, "targetAssetId");
            if (!TryGetUnique(entry, "targetModId", out var owner) || owner.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) ||
                (owner.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(owner.GetString())) ||
                !AssetId.TryParse(targetText, out var target) || target == default ||
                !AssetId.TryParse(GetString(entry, "replacementAssetId"), out var replacement) || replacement == default)
                return null;
            return new(owner.GetString(), target, replacement);
        }
        catch (InvalidOperationException) { return null; } // Invalid decoded text in this record only.
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetUnique(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetUnique(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(name)) continue;
            if (value.ValueKind != JsonValueKind.Undefined) return false;
            value = property.Value;
        }
        return value.ValueKind != JsonValueKind.Undefined;
    }
}

namespace Karpik.Content.Core;

public enum ContentDiagnosticSeverity
{
    Error = 0,
    Warning = 1
}

public sealed class ContentDiagnostic : IEquatable<ContentDiagnostic>
{
    public string Code { get; }
    public ContentDiagnosticSeverity Severity { get; }
    public string? RelativePath { get; }
    public string Message { get; }

    public ContentDiagnostic(string code, ContentDiagnosticSeverity severity, string? relativePath, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Severity = severity;
        RelativePath = relativePath;
        Message = message;
    }

    public bool Equals(ContentDiagnostic? other)
    {
        if (other is null) return false;
        return Code == other.Code
               && Severity == other.Severity
               && RelativePath == other.RelativePath
               && Message == other.Message;
    }

    public override bool Equals(object? obj) => obj is ContentDiagnostic other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Code, Severity, RelativePath, Message);

    public override string ToString()
    {
        string location = RelativePath is null
            ? string.Empty
            : $"{RelativePath}: ";
        return $"{Code} [{Severity}] {location}{Message}";
    }
}

public static class ContentDiagnosticCodes
{
    public const string InvalidMetaJson = "KCO001";
    public const string MissingMetaField = "KCO002";
    public const string InvalidAssetId = "KCO003";
    public const string DuplicateAssetId = "KCO004";
    public const string DuplicateLogicalName = "KCO005";
    public const string InvalidLogicalName = "KCO006";
    public const string NamespaceMismatch = "KCO007";
    public const string UnsupportedDeclaredType = "KCO008";
    public const string MissingSourceFile = "KCO009";
    public const string MissingMetaFile = "KCO010";
    public const string InvalidJsonContent = "KCO011";
    public const string UnknownDependency = "KCO012";
    public const string DependencyOutsideNamespace = "KCO013";
    public const string DependencyCycle = "KCO014";
    public const string PathTraversal = "KCO015";
    public const string InvalidMetaSchemaVersion = "KCO016";
    public const string ArtifactHashMismatch = "KCO017";
    public const string ManifestCorrupt = "KCO018";
    public const string InvalidManifestSchemaVersion = "KCO019";
    public const string InvalidImageContent = "KCO020";
    public const string InvalidShaderContent = "KCO021";
}

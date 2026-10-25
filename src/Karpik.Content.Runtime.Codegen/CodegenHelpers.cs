using System;
using System.Text;

namespace Karpik.Content.Codegen;

internal static class CodegenHelpers
{
    public static string ToFieldName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            return string.Empty;
        }

        string[] parts = logicalName.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = ToPascalCaseSegment(parts[i]);
        }

        return string.Join("_", parts);
    }

    public static string ToPascalCaseSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return string.Empty;
        }

        string normalized = segment.Replace('-', '_').Replace('.', '_');
        string[] subparts = normalized.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < subparts.Length; i++)
        {
            string sub = subparts[i];
            if (sub.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(sub[0]));
            if (sub.Length > 1)
            {
                sb.Append(sub.Substring(1));
            }
            if (i < subparts.Length - 1)
            {
                sb.Append('_');
            }
        }

        if (sb.Length == 0 && segment.Length > 0)
        {
            sb.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1) sb.Append(segment.Substring(1));
        }

        return sb.ToString();
    }

    public static bool IsValidLogicalName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) return false;
        if (logicalName.IndexOf('\\') >= 0) return false;
        if (logicalName.StartsWith("/") || logicalName.EndsWith("/")) return false;
        if (logicalName.Contains("//")) return false;
        if (logicalName.Contains("..")) return false;
        if (logicalName.IndexOf('/') < 0) return false;
        foreach (char c in logicalName)
        {
            if (c == '/') continue;
            if (c >= 'a' && c <= 'z') continue;
            if (c >= 'A' && c <= 'Z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c == '_' || c == '-' || c == '.') continue;
            return false;
        }

        string[] segs = logicalName.Split('/');
        foreach (string seg in segs)
        {
            if (seg.Length == 0) return false;
            if (seg == "." || seg == "..") return false;
        }

        return true;
    }

    public static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

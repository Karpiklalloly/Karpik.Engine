using System.Text;

namespace Karpik.Engine.Shared;

public static class AssetPath
{
    public static int GetHash(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;

        string normalized = path.ToLowerInvariant().Replace('\\', '/');
        byte[] bytes = Encoding.UTF8.GetBytes(normalized);

        const int fnvPrime = 16777619;
        int hash = unchecked((int)2166136261);

        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }

        return hash;
    }
}
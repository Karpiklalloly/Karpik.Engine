namespace Karpik.Content.Core;

[Flags]
public enum AssetTarget
{
    Client = 1,
    Server = 2,
    Shared = Client | Server
}

public static class AssetTargets
{
    public static bool TryParse(string? text, out AssetTarget target)
    {
        if (string.Equals(text, "Client", StringComparison.OrdinalIgnoreCase))
        {
            target = AssetTarget.Client;
            return true;
        }

        if (string.Equals(text, "Server", StringComparison.OrdinalIgnoreCase))
        {
            target = AssetTarget.Server;
            return true;
        }

        target = default;
        return false;
    }

    public static string ToCanonicalString(this AssetTarget target) => target switch
    {
        AssetTarget.Client => "Client",
        AssetTarget.Server => "Server",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Only single targets have a canonical string.")
    };
}

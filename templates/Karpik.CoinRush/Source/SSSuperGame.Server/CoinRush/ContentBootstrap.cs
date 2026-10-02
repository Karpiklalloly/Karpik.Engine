using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Registers the server content manifest with the runtime content store.</summary>
public static class ContentBootstrap
{
    private static bool _registered;

    /// <summary>Registers the manifest once when it is available.</summary>
    /// <param name="registry">The content registry that receives the manifest.</param>
    /// <param name="store">The store used to resolve manifest assets.</param>
    /// <param name="fs">The file system used to locate the manifest.</param>
    /// <param name="log">The logger for missing or invalid content.</param>
    /// <param name="owner">The log label for the caller.</param>
    public static void EnsureManifest(
        IContentRegistry registry,
        IContentStore store,
        IFileSystem fs,
        ILogger log,
        string owner)
    {
        if (_registered)
        {
            return;
        }
        try
        {
            ContentManifest? manifest = LoadManifest(fs, log, owner);
            if (manifest is null)
            {
                return;
            }
            registry.RegisterManifest(manifest, store);
            _registered = true;
            log.LogInformation("[{Owner}] content manifest registered.", owner);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[{Owner}] manifest registration failed.", owner);
        }
    }

    /// <summary>Reads the manifest and logs any content diagnostics.</summary>
    /// <param name="fs">The file system containing the content directory.</param>
    /// <param name="log">The logger for manifest diagnostics.</param>
    /// <param name="owner">The log label for the caller.</param>
    /// <returns>The manifest, or <see langword="null"/> when its file is absent.</returns>
    private static ContentManifest? LoadManifest(IFileSystem fs, ILogger log, string owner)
    {
        string manifestPath = fs.Combine(fs.ContentPath, "manifest.json");
        if (!fs.Exists(manifestPath))
        {
            log.LogWarning("[{Owner}] manifest not found at {Path}, content fallback in use.", owner, manifestPath);
            return null;
        }
        var diagnostics = new List<ContentDiagnostic>();
        ContentManifest manifest = ContentManifest.LoadFromFile(manifestPath, diagnostics);
        foreach (ContentDiagnostic d in diagnostics)
        {
            log.LogWarning("[{Owner}] manifest diagnostic: {Diag}.", owner, d);
        }
        return manifest;
    }
}

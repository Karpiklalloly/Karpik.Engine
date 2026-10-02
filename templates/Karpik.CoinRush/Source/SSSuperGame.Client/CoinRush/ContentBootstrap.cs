using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Registers the cooked content manifest before client assets are requested.</summary>
public static class ContentBootstrap
{
    private static bool _registered;

    /// <summary>Loads and registers the manifest once, logging recoverable failures.</summary>
    /// <param name="registry">Registry that resolves cooked assets.</param>
    /// <param name="store">Store containing cooked asset payloads.</param>
    /// <param name="fs">File system used to locate the manifest.</param>
    /// <param name="log">Logger for registration diagnostics.</param>
    /// <param name="owner">Name included in diagnostics.</param>
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

    /// <summary>Reads the manifest and reports diagnostics if it exists.</summary>
    /// <param name="fs">File system used to locate the manifest.</param>
    /// <param name="log">Logger for manifest diagnostics.</param>
    /// <param name="owner">Name included in diagnostics.</param>
    /// <returns>The manifest, or <see langword="null"/> when the file is absent.</returns>
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

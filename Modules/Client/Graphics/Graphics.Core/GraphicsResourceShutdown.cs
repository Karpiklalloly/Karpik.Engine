namespace Karpik.Engine.Client.Graphics.Core;

/// <summary>Cold shutdown: attempt all native cleanup and always release CPU claims, even on a lost device.</summary>
internal static class GraphicsResourceShutdown
{
    public static void Dispose(Action waitForIdle, Action releaseCpuClaims, params IDisposable?[] resources)
    {
        List<Exception>? errors = null;
        try
        {
            try { waitForIdle(); } catch (Exception ex) { (errors ??= []).Add(ex); }
            foreach (var resource in resources)
                try { resource?.Dispose(); } catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        finally
        {
            try { releaseCpuClaims(); } catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (errors != null) throw new AggregateException(errors);
    }
}

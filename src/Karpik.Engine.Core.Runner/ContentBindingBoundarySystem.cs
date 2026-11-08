using Karpik.Content.Runtime;

namespace Karpik.Engine.Core.Runner;

/// <summary>Runs stable, scoped binding owners before simulation consumers.</summary>
internal sealed class ContentBindingBoundarySystem(IEnumerable<IContentBindingRuntime> runtimes)
    : ISystemMainThreadFrameBegin, ISystemAsyncDestroy
{
    private readonly IContentBindingRuntime[] _runtimes = runtimes.ToArray();
    private int _stopped;

    public void MainThreadFrameBegin()
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        for (int i = 0; i < _runtimes.Length; i++) _runtimes[i].AdvanceBoundary();
    }

    public async ValueTask DestroyAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        List<Exception>? errors = null;
        for (int i = 0; i < _runtimes.Length; i++)
        {
            try { _runtimes[i].RequestStop(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        for (int i = 0; i < _runtimes.Length; i++)
        {
            try { await _runtimes[i].DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors != null) throw new AggregateException("Content binding shutdown failed.", errors);
    }
}

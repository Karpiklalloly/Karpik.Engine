using Autofac;
using Autofac.Core;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Microsoft.Extensions.Logging;
using Karpik.Engine.Core.Runner;

namespace Karpik.Engine.Core;

public partial class EngineRunner
{
    private ContentReloadCoordinator? _contentCoordinator;
    private ContentReloadHostRuntime? _contentEndpoint;
    private Func<Task<ModContentRefreshResult>>? _requestModContentRefresh;

    private void RegisterContentStartup(ContainerBuilder builder)
    {
        builder.Register(_ => new EmptyContentChangeSource()).As<IContentChangeSource>()
            .As<IContentReloadLifetime>().SingleInstance();
        builder.RegisterInstance(new DeferredContentStartup());
        builder.RegisterInstance(this).As<IModContentRefresh>().ExternallyOwned();
    }

    private static void RegisterContentPublication(ContainerBuilder builder)
    {
        builder.Register(context => new ContentReloadCoordinator(context.Resolve<ContentRegistry>()))
            .AsSelf().As<IContentChangeSource>().As<IContentReloadLifetime>().SingleInstance()
            .OnlyIf(registry => registry.IsRegistered(new TypedService(typeof(ContentRegistry))));
        // The typed factory also works when Static suppresses reflected system registration.
        builder.Register(context => new ContentReloadBoundarySystem(context.Resolve<ContentReloadCoordinator>()))
            .AsSelf().SingleInstance();
    }

    private async ValueTask<ModContentRefreshCapture?> CompleteContentStartupAsync(SystemRegistry systems,
        CancellationToken cancellation)
    {
        var participant = _modSetScope!.ResolveOptional<IModContentParticipant>();
        if (_engineContainer!.ResolveOptional<ContentRegistry>() is not { } content)
        {
            participant?.CompleteStartupWithoutContent(cancellation);
            return null;
        }
        var (captures, owners) = participant is null ? ([], [])
            : await participant.CaptureStartupAsync(ReportContentDiagnostic, cancellation);
        Exception? primaryFailure = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            content.CompleteStartup(captures.Select(static capture => capture.Package).ToArray(), owners, ReportContentDiagnostic,
                participant is null ? null : packages => participant.ValidateStartupPackages(packages, cancellation));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally { ModContentCapture.DisposeAll(captures, primaryFailure); }
        var refreshCapture = participant?.CreateRefreshCapture(ReportContentDiagnostic);
        // Both sides publish at the existing quiescent boundary before binding owners advance.
        if (refreshCapture is not null || _contentReloadInstanceId is not null)
            systems.Add<ContentReloadBoundarySystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -2000);
        return refreshCapture;
    }

    private void OpenContentReloadAdmission(ModContentRefreshCapture? refreshCapture)
    {
        _contentEndpoint = _engineContainer!.ResolveOptional<ContentReloadHostRuntime>();
        _contentEndpoint?.Start();
        if (_contentCoordinator is { } coordinator && refreshCapture is not null)
            Volatile.Write(ref _requestModContentRefresh, () => RequestModContentRefreshCoreAsync(coordinator, refreshCapture));
    }

    private void ReportContentDiagnostic(ContentDiagnostic diagnostic) =>
        _runtimeLogger.Log(diagnostic.Severity == ContentDiagnosticSeverity.Error ? LogLevel.Error : LogLevel.Warning,
            "{Code}: {Path}: {Message}", diagnostic.Code, diagnostic.RelativePath, diagnostic.Message);

    /// <summary>Queue a local mod-content refresh for the next quiescent publication boundary.</summary>
    public Task<ModContentRefreshResult> RequestModContentRefreshAsync() =>
        Volatile.Read(ref _requestModContentRefresh)?.Invoke() ??
        Task.FromResult(new ModContentRefreshResult(ModContentRefreshStatus.Unavailable));

    private async Task<ModContentRefreshResult> RequestModContentRefreshCoreAsync(ContentReloadCoordinator coordinator,
        ModContentRefreshCapture capture)
    {
        var result = await coordinator.EnqueueModRefreshAsync(capture.Invoke, ReportContentDiagnostic).ConfigureAwait(false);
        return new(result.Outcome switch
        {
            ContentReloadOutcome.Applied => ModContentRefreshStatus.Applied,
            ContentReloadOutcome.Busy => ModContentRefreshStatus.Busy,
            _ => ModContentRefreshStatus.Failed
        }, result.Error);
    }

    void IEngineRunner.StopContentReloadAdmission() => StopContentReloadAdmission();

    private void StopContentReloadAdmission()
    {
        Interlocked.Exchange(ref _requestModContentRefresh, null);
        _contentCoordinator?.RequestStop();
        _contentEndpoint?.RequestStop();
    }
}

using System.IO.Pipes;
using System.Text.Json;

namespace Karpik.Content.Transport;

/// <summary>
/// Explicitly activated, same-user development endpoint. All work is cold control-plane work.
/// Its bounded worker slots remain occupied until accepted handlers settle, even if clients disconnect.
/// Only cooperating processes on verified local filesystems may share its trusted output namespace.
/// </summary>
public sealed class ContentReloadEndpoint : IAsyncDisposable
{
    private readonly ContentReloadAddress _address;
    private readonly Func<ContentReloadRequest, CancellationToken, Task<ContentReloadResponse>> _handler;
    private readonly TimeSpan _messageTimeout;
    private readonly CancellationTokenSource _stopping = new();
    private readonly FileStream _ownership;
    private readonly Task[] _workers;
    private readonly object _disposeGate = new();
    private readonly object _applicationsGate = new();
    private readonly List<Task> _unsettled = [];
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _disposeTask;
    private Exception? _failure;

    /// <summary>Completes after accepted applications have settled and target ownership is released.</summary>
    public Task Completion => _completion.Task;

    public ContentReloadEndpoint(ContentReloadAddress address,
        Func<ContentReloadRequest, CancellationToken, Task<ContentReloadResponse>> handler,
        int maximumConnections = 8, TimeSpan? messageTimeout = null)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Content reload transport is supported on verified Windows/Linux local filesystems.");
        ContentReloadWire.ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(handler);
        if (maximumConnections is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumConnections));
        _messageTimeout = messageTimeout ?? TimeSpan.FromSeconds(5);
        if (_messageTimeout <= TimeSpan.Zero || _messageTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(messageTimeout));
        _address = address;
        _handler = handler;
        Directory.CreateDirectory(Path.GetDirectoryName(address.LockPath)!);
        // Never unlink/replace this file: all owners must agree on one filesystem object.
        _ownership = new FileStream(address.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _workers = new Task[maximumConnections];
        var servers = new NamedPipeServerStream[maximumConnections];
        try
        {
            // Bind every listener before returning; failure leaves no partially active endpoint.
            for (int i = 0; i < servers.Length; i++) servers[i] = CreateServer();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            for (int i = 0; i < servers.Length; i++) _workers[i] = WorkerAsync(servers[i], started.Task);
            started.SetResult();
        }
        catch
        {
            foreach (var server in servers) server?.Dispose();
            _ownership.Dispose();
            _stopping.Dispose();
            throw;
        }
    }

    private NamedPipeServerStream CreateServer() => new(_address.PipeName, PipeDirection.InOut, _workers.Length,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task WorkerAsync(NamedPipeServerStream first, Task started)
    {
        NamedPipeServerStream? pipe = first;
        try
        {
            // A preconnected client cannot fault a worker before the complete worker array exists.
            await started.ConfigureAwait(false);
            while (!_stopping.IsCancellationRequested)
            {
                await pipe!.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                await ServeAsync(pipe).ConfigureAwait(false);
                pipe.Dispose();
                pipe = null;
                if (!_stopping.IsCancellationRequested) pipe = CreateServer();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (IOException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref _failure, error, null);
            _ = DisposeAsync();
        }
        finally { pipe?.Dispose(); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        ContentReloadRequest request = default;
        ContentReloadResponse response;
        bool accepted = false;
        try
        {
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
            {
                readTimeout.CancelAfter(_messageTimeout);
                byte[] bytes = await ContentReloadWire.ReadAsync(pipe, readTimeout.Token).ConfigureAwait(false);
                request = JsonSerializer.Deserialize(bytes, ContentReloadJsonContext.Default.ContentReloadRequest);
            }
            string? validation = ContentReloadWire.ValidateRequest(_address, request);
            if (validation != null) response = ContentReloadWire.Failure(request.RequestId, validation);
            else
            {
                _stopping.Token.ThrowIfCancellationRequested();
                accepted = true;
                // A callback cannot hold an accept worker synchronously before returning its Task.
                Task<ContentReloadResponse> application = Task.Run(() => _handler(request, _stopping.Token));
                try { response = await application.WaitAsync(_stopping.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    // At most one observed unfinished application per terminally stopped worker.
                    // No new requests are accepted after this point. Coordinator owns its own shutdown/join.
                    lock (_applicationsGate) _unsettled.Add(application);
                    response = ContentReloadWire.Unknown(request.RequestId, "Endpoint stopped while application outcome remained unknown.");
                }
                if (!ContentReloadWire.ValidResponse(response, request.RequestId))
                    response = ContentReloadWire.Unknown(request.RequestId, "Application returned an invalid or uncorrelated response; application outcome unknown.");
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            response = accepted
                ? ContentReloadWire.Unknown(request.RequestId, "Endpoint stopped while application outcome remained unknown.")
                : ContentReloadWire.Failure(request.RequestId, "Endpoint is shutting down.");
        }
        catch (OperationCanceledException)
        {
            response = accepted
                ? ContentReloadWire.Unknown(request.RequestId, "Accepted application was canceled without a terminal outcome.")
                : ContentReloadWire.Failure(request.RequestId, "Content message read deadline exceeded.");
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            response = accepted
                ? ContentReloadWire.Unknown(request.RequestId, "Accepted application raised an exception; application outcome unknown: " + error.Message)
                : ContentReloadWire.Failure(request.RequestId, error.Message);
        }
        catch (Exception error)
        {
            response = accepted
                ? ContentReloadWire.Unknown(request.RequestId, "Accepted application raised an exception; application outcome unknown: " + error.Message)
                : ContentReloadWire.Failure(request.RequestId, error.Message);
        }

        try
        {
            using var writeTimeout = new CancellationTokenSource(_messageTimeout);
            await ContentReloadWire.WriteAsync(pipe,
                JsonSerializer.SerializeToUtf8Bytes(response, ContentReloadJsonContext.Default.ContentReloadResponse), writeTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        { /* A disconnected client does not change the already accepted application outcome. */ }
    }

    /// <summary>
    /// Stops accepting, cancels the endpoint lifetime token and answers unresolved accepted requests with Unknown.
    /// Bounded disposal does not imply an uncooperative handler has stopped: await Completion after settling its coordinator.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new ValueTask(_disposeTask ??= Task.Run(StopAsync));
    }
    private async Task StopAsync()
    {
        try { _stopping.Cancel(); }
        catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); }
        try { await Task.WhenAll(_workers).ConfigureAwait(false); }
        finally
        {
            // Workers no longer mutate this bounded collection. Keep ownership until every accepted callback settles.
            Task[] applications;
            lock (_applicationsGate) applications = _unsettled.ToArray();
            _ = ReleaseOwnershipAsync(applications);
        }
    }

    private async Task ReleaseOwnershipAsync(Task[] applications)
    {
        try { await Task.WhenAll(applications).ConfigureAwait(false); }
        catch { /* Observe late handler faults; its coordinator owns the application result. */ }
        try
        {
            _ownership.Dispose();
            _stopping.Dispose();
            if (_failure is { } failure) _completion.TrySetException(failure);
            else _completion.TrySetResult();
        }
        catch (Exception error) { _completion.TrySetException(error); }
    }
}

public static class ContentReloadClient
{
    /// <summary>A deadline or disconnected accepted request has Unknown outcome; waiting never cancels runtime application.</summary>
    public static async Task<ContentReloadResponse> DeliverAsync(ContentReloadAddress address, ContentReloadRequest request,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ContentReloadWire.ValidateAddress(address);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        string? invalid = ContentReloadWire.ValidateRequest(address, request);
        if (invalid != null) return ContentReloadWire.Failure(request.RequestId, invalid);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, ContentReloadJsonContext.Default.ContentReloadRequest);
        if (payload.Length > ContentReloadWire.MaximumMessageBytes)
            return ContentReloadWire.Failure(request.RequestId, "Content message exceeds its size limit.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var pipe = new NamedPipeClientStream(".", address.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        bool possiblySubmitted = false;
        try
        {
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            possiblySubmitted = true;
            await ContentReloadWire.WriteAsync(pipe, payload, deadline.Token).ConfigureAwait(false);
            byte[] responseBytes = await ContentReloadWire.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize(responseBytes, ContentReloadJsonContext.Default.ContentReloadResponse);
            return ContentReloadWire.ValidResponse(response, request.RequestId)
                ? response
                : ContentReloadWire.Unknown(request.RequestId, "Invalid or uncorrelated endpoint response; application outcome unknown.");
        }
        catch (OperationCanceledException)
        { return ContentReloadWire.Unknown(request.RequestId, "Delivery wait deadline/cancellation reached; application outcome unknown."); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return possiblySubmitted
                ? ContentReloadWire.Unknown(request.RequestId, "Delivery connection failed after submission; application outcome unknown: " + error.Message)
                : ContentReloadWire.Failure(request.RequestId, "Content endpoint is unavailable: " + error.Message);
        }
    }
}

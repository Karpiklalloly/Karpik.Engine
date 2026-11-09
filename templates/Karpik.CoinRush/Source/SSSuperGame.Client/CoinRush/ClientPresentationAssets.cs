using System.Composition;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Owns live binding handles and the legacy font for one client simulation.</summary>
[Export(typeof(ClientPresentationAssets))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
[AutoContentBindings]
public sealed partial class ClientPresentationAssets(
    IContentBindings bindings,
    IAssetsManager assets,
    ILogger<ClientPresentationAssets> log) : IAsyncDisposable
{
    private readonly UiStrings _fallbackUi = CoinRushContent.DefaultUi();
    private ClientDrawState _handles;
    private JobHandle<AssetHandle<Asset>>? _fontJob;
    private AssetHandle<Asset> _fontHandle;
    private IFont? _font;
    private Task<ClientDrawState>? _initialization;
    private Task? _disposal;
    private volatile bool _disposed;

    /// <summary>Creates bindings and completes legacy font loading before entering the frame loop.</summary>
    public ValueTask<ClientDrawState> InitializeAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ValueTask<ClientDrawState>(_initialization ??= InitializeCoreAsync(ct));
    }

    private async Task<ClientDrawState> InitializeCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        InitializeGeneratedBindings(bindings);
        _handles = new ClientDrawState
        {
            Ui = Game_CoinRush_UiStringsBinding,
            Player = Game_Sprites_PlayerBinding,
            Coin = Game_CoinRush_CoinBinding,
            Tile = Game_CoinRush_TileBinding
        };

        try
        {
            _fontJob = assets.LoadAssetByPathAsync(ContentRefs.Game_PressStart_Font_SourcePath);
            AssetHandle<Asset> handle = await _fontJob.Value;
            if (_disposed || ct.IsCancellationRequested)
            {
                handle.Dispose();
                ct.ThrowIfCancellationRequested();
                return _handles;
            }
            _fontHandle = handle;
            _font = (handle.Asset as FontAsset)?.Font;
            if (_font is not null) log.LogInformation("Font ready, HUD text enabled.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            // Texture/shape presentation remains available when the optional legacy font fails.
            log.LogWarning(error, "CoinRush font could not be loaded; HUD text is unavailable.");
        }
        finally
        {
            _fontJob?.Dispose();
            _fontJob = null;
        }
        return _handles;
    }

    [AutoBindJson("game/CoinRush/UiStrings")]
    private (System.Text.Json.Serialization.Metadata.JsonTypeInfo<UiStrings>, UiStrings) DescribeUi() =>
        (ClientPresentationJsonContext.CreateUiTypeInfo(), _fallbackUi);

    /// <summary>Returns borrowed values valid only for the current presentation frame.</summary>
    public ClientPresentationFrame Read(in ClientDrawState handles)
    {
        if (_disposed) return default;
        UiStrings ui = bindings.TryGet(handles.Ui, out UiStrings current) ? current : _fallbackUi;
        bindings.TryGet(handles.Player, out ITexture2D player);
        bindings.TryGet(handles.Coin, out ITexture2D coin);
        bindings.TryGet(handles.Tile, out ITexture2D tile);
        return new ClientPresentationFrame(ui, player, coin, tile, _font);
    }

    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _fontJob?.Cancel();
        if (_initialization is not null)
        {
            try { await _initialization; }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Startup failure still must release any registered bindings. */ }
        }
        ReleaseGeneratedBindings();
        _handles = default;
        _font = null;
        _fontHandle.Dispose();
        _fontHandle = default;
    }
}

/// <summary>A stack-local borrowed view; never store this managed-reference value in ECS.</summary>
public readonly struct ClientPresentationFrame(
    UiStrings ui, ITexture2D? player, ITexture2D? coin, ITexture2D? tile, IFont? font)
{
    public readonly UiStrings Ui = ui;
    public readonly ITexture2D? PlayerTex = player;
    public readonly ITexture2D? CoinTex = coin;
    public readonly ITexture2D? TileTex = tile;
    public readonly IFont? Font = font;
}

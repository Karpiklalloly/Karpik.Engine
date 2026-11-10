using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DCFApixels.DragonECS;
using Karpik.Content.Core;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using SSSuperGame.Client.CoinRush;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks that presentation state remains safe to keep in ECS.</summary>
[Collection(nameof(PresentationGraphicsCollection))]
public sealed class PresentationBindingTests
{
    [Fact]
    public void Draw_state_contains_only_value_handles()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ClientDrawState>());
    }

    [Fact]
    public async Task Same_draw_system_records_updated_title_after_publication()
    {
        await using var scene = new PresentationScene(withFont: true);
        var world = new EcsDefaultWorld();
        ClientDrawSystem? draw = null;
        try
        {
            int match = world.NewEntity();
            world.GetPool<MatchState>().Add(match) = new MatchState { Phase = MatchPhase.WaitingForPlayers };
            draw = new ClientDrawSystem(world, new GraphicsCameraState(), new StubWindow(),
                scene.Owner, new Karpik.Engine.Core.Time());
            await draw.InitAsync(TestContext.Current.CancellationToken);
            await scene.Start();
            AssertRecordedTitle(draw, "Initial title", "Updated title");

            await scene.Update(scene.Ui with { Title = "Updated title" }, coinWidth: 2);
            await scene.PumpUntil(() => scene.Owner.Read(in scene.Handles).Ui.Title == "Updated title");
            AssertRecordedTitle(draw, "Updated title", "Initial title");
        }
        finally
        {
            // The fake font atlas is untracked; texture commands (if any) must
            // release their frame claims before owner/registry disposal.
            ((ThreadBuffer)GraphicsContext.Buffer).Clear();
            if (draw is not null) await draw.DestroyAsync();
            world.Destroy();
        }
    }

    private static void AssertRecordedTitle(ClientDrawSystem draw, string expected, string obsolete)
    {
        var buffer = (ThreadBuffer)GraphicsContext.Buffer;
        buffer.Clear();
        try
        {
            draw.RenderPrepare();
            ReadOnlySpan<DrawTextCmd> commands = buffer.GetTextCommands();
            bool found = false;
            for (int i = 0; i < commands.Length; i++)
            {
                ReadOnlySpan<char> text = commands[i].Text.Span;
                if (text.SequenceEqual(expected)) found = true;
                Assert.False(text.SequenceEqual(obsolete));
            }
            Assert.True(found, "RenderPrepare did not record the expected live title.");
        }
        finally { buffer.Clear(); }
    }

    [Fact]
    public async Task Presentation_reads_new_UI_and_texture_and_uses_empty_on_removal()
    {
        await using var scene = new PresentationScene();
        await scene.Start();
        ClientPresentationFrame before = scene.Owner.Read(in scene.Handles);
        Assert.Equal("Initial title", before.Ui.Title);
        Assert.Equal(1u, before.CoinTex!.Width);

        await scene.Update(scene.Ui with { Title = "Updated title" }, coinWidth: 2);
        await scene.PumpUntil(() => scene.Owner.Read(in scene.Handles).Ui.Title == "Updated title");
        ClientPresentationFrame after = scene.Owner.Read(in scene.Handles);
        Assert.Equal(2u, after.CoinTex!.Width);
        Assert.NotSame(before.CoinTex, after.CoinTex);

        await scene.Update(scene.Ui, coinWidth: 2, includeCoin: false);
        Assert.Null(scene.Owner.Read(in scene.Handles).CoinTex);
        await scene.Update(scene.Ui, coinWidth: 3);
        await scene.PumpUntil(() => scene.Owner.Read(in scene.Handles).CoinTex?.Width == 3);
    }

    [Fact]
    public async Task Invalid_UI_projection_preserves_the_last_good_title()
    {
        await using var scene = new PresentationScene();
        await scene.Start();
        await scene.UpdateRawUi("{}");
        await scene.PumpUntil(() => scene.Bindings.TryGetStatus(scene.Handles.Ui, out var status) &&
            status.State == ContentBindingState.Failed);
        Assert.Equal("Initial title", scene.Owner.Read(in scene.Handles).Ui.Title);
        Assert.NotNull(scene.Bindings.GetError(scene.Handles.Ui));
        await scene.Update(scene.Ui with { Title = "Recovered title" }, coinWidth: 1);
        await scene.PumpUntil(() => scene.Owner.Read(in scene.Handles).Ui.Title == "Recovered title");
    }

    [Fact]
    public void UI_metadata_rejects_null_and_oversized_labels_before_they_reach_render()
    {
        var typeInfo = ClientPresentationJsonContext.CreateUiTypeInfo();
        var sourceInfo = new ClientPresentationJsonContext().UiStrings;
        UiStrings defaults = CoinRushContent.DefaultUi();
        JsonNode nullUi = JsonNode.Parse(JsonSerializer.Serialize(defaults, sourceInfo))!;
        nullUi["Title"] = null;
        string nullTitle = nullUi.ToJsonString();
        string oversized = JsonSerializer.Serialize(defaults with
        {
            Player0 = new string('x', ClientHudText.MaximumFormattedLength)
        }, sourceInfo);
        // Each combined label fits the 2048 stack buffer, but together they
        // would overflow a fresh simulation thread's 4096 copied characters.
        string aggregateOverflow = JsonSerializer.Serialize(defaults with
        {
            Player0 = new string('a', 900), Player1 = new string('b', 900)
        }, sourceInfo);
        string hintOverflow = JsonSerializer.Serialize(defaults with
        {
            ControlsHint = new string('c', 2048), RestartHint = new string('r', 2048)
        }, sourceInfo);
        string largeValid = JsonSerializer.Serialize(defaults with
        {
            Player0 = new string('a', 700), Player1 = new string('b', 700)
        }, sourceInfo);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(nullTitle, typeInfo));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(oversized, typeInfo));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(aggregateOverflow, typeInfo));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(hintOverflow, typeInfo));
        Assert.NotNull(JsonSerializer.Deserialize(largeValid, typeInfo));
    }

    [Fact]
    public async Task Ready_presentation_reads_and_dynamic_HUD_formatting_allocate_nothing()
    {
        await using var scene = new PresentationScene();
        await scene.Start();
        var match = new MatchState { Phase = MatchPhase.Countdown, TimeLeft = 2.5f, Score0 = int.MinValue, Score1 = int.MaxValue };
        // All allocations and generic/JIT warm-up precede the measured frame-read path.
        WarmReads(scene, in match, 100);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = WarmReads(scene, in match, 10_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(count > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task Presentation_disposal_releases_all_handles_and_is_idempotent()
    {
        await using var scene = new PresentationScene();
        await scene.Start();
        await scene.Owner.DisposeAsync();
        await scene.Owner.DisposeAsync();
        Assert.False(scene.Bindings.TryGet(scene.Handles.Ui, out _));
        Assert.False(scene.Bindings.TryGet(scene.Handles.Player, out _));
        Assert.False(scene.Bindings.TryGet(scene.Handles.Coin, out _));
        Assert.False(scene.Bindings.TryGet(scene.Handles.Tile, out _));
    }

    [Fact]
    public void Dynamic_HUD_text_preserves_output_and_reports_small_buffers()
    {
        UiStrings ui = CoinRushContent.DefaultUi();
        var match = new MatchState { Phase = MatchPhase.Countdown, TimeLeft = 2.1f, Score0 = -12, Score1 = 42 };
        Span<char> text = stackalloc char[ClientHudText.MaximumFormattedLength];
        Assert.True(ClientHudText.TryPhase(text, ui, in match, out int phase));
        Assert.Equal(ui.Countdown + " 3", text[..phase].ToString());
        Assert.True(ClientHudText.TryScore(text, ui, in match, out int score));
        Assert.Equal(ui.Player0 + " -12 : 42 " + ui.Player1, text[..score].ToString());
        Assert.False(ClientHudText.TryPlayerLabel(text[..1], ui.Player0, int.MaxValue, out _));
    }

    private static int WarmReads(PresentationScene scene, in MatchState match, int iterations)
    {
        Span<char> text = stackalloc char[ClientHudText.MaximumFormattedLength];
        int count = 0;
        for (int i = 0; i < iterations; i++)
        {
            ClientPresentationFrame frame = scene.Owner.Read(in scene.Handles);
            ClientHudText.TryScore(text, frame.Ui, in match, out int score);
            ClientHudText.TryPhase(text, frame.Ui, in match, out int phase);
            ClientHudText.TryPlayerLabel(text, frame.Ui.Player0, i, out int label);
            count += score + phase + label;
        }
        return count;
    }

    private sealed class PresentationScene : IAsyncDisposable
    {
        private readonly MemoryStore _store = new();
        private readonly ContentRegistry _registry;
        private readonly ContentReloadCoordinator _reload;
        private readonly AssetsManager _legacyAssets;
        private readonly string? _fontRoot;
        private readonly Dictionary<AssetId, string> _locators = new();
        private int _revision;
        public readonly ContentBindings Bindings;
        public readonly ClientPresentationAssets Owner;
        public ClientDrawState Handles;
        public UiStrings Ui = CoinRushContent.DefaultUi() with { Title = "Initial title" };

        public PresentationScene(bool withFont = false)
        {
            SetUi(Ui);
            SetTexture(ContentRefs.Game_Sprites_Player.Id, 1);
            SetTexture(ContentRefs.Game_CoinRush_Coin.Id, 1);
            SetTexture(ContentRefs.Game_CoinRush_Tile.Id, 1);
            _registry = new ContentRegistry(null!, _store, [new TexturePayloadLoader()]);
            _registry.RegisterManifest(Manifest(), _store);
            _reload = new ContentReloadCoordinator(_registry);
            Bindings = new ContentBindings(_registry, _registry, _reload);
            Karpik.Engine.Core.FileSystem.IFileSystem fileSystem = new Karpik.Engine.Core.FileSystem.PhysicalFileSystem();
            if (withFont)
            {
                _fontRoot = Path.Combine(Path.GetTempPath(), "CoinRush-Presentation-" + Guid.NewGuid().ToString("N"));
                string content = Path.Combine(_fontRoot, "Content");
                Assert.False(Path.IsPathRooted(ContentRefs.Game_PressStart_Font_SourcePath));
                string fontPath = Path.Combine(content, ContentRefs.Game_PressStart_Font_SourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
                File.WriteAllText(fontPath, "test font loader owns the format");
                fileSystem = new RemappedFileSystem(_fontRoot, content);
            }
            _legacyAssets = new AssetsManager(NullLogger<AssetsManager>.Instance, fileSystem, _registry, [],
                withFont ? [new StubFontLoader()] : []);
            Owner = new ClientPresentationAssets(Bindings, _legacyAssets, NullLogger<ClientPresentationAssets>.Instance);
        }

        public async Task Start()
        {
            Handles = await Owner.InitializeAsync(TestContext.Current.CancellationToken);
            await PumpUntil(() => Bindings.TryGetStatus(Handles.Ui, out var status) &&
                status.State == ContentBindingState.Ready && Owner.Read(in Handles).CoinTex is not null &&
                Owner.Read(in Handles).PlayerTex is not null && Owner.Read(in Handles).TileTex is not null);
        }

        public async Task Update(UiStrings ui, byte coinWidth, bool includeCoin = true)
        {
            Ui = ui;
            SetUi(ui);
            SetTexture(ContentRefs.Game_CoinRush_Coin.Id, coinWidth);
            await Publish(includeCoin);
        }

        public async Task UpdateRawUi(string json)
        {
            SetBytes(ContentRefs.Game_CoinRush_UiStrings.Id, System.Text.Encoding.UTF8.GetBytes(json));
            await Publish(includeCoin: true);
        }

        private async Task Publish(bool includeCoin)
        {
            Task<ContentReloadResult> delivery = _reload.EnqueueAsync(Manifest(includeCoin), _store);
            await PumpUntil(() => delivery.IsCompleted);
            Assert.Equal(ContentReloadOutcome.Applied, (await delivery).Outcome);
        }

        public async Task PumpUntil(Func<bool> ready)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!ready())
            {
                _reload.PublishBoundary();
                Bindings.AdvanceBoundary();
                await Task.Delay(1, timeout.Token);
            }
        }

        private void SetUi(UiStrings ui) => SetBytes(ContentRefs.Game_CoinRush_UiStrings.Id,
            JsonSerializer.SerializeToUtf8Bytes(ui, new ClientPresentationJsonContext().UiStrings));
        private void SetTexture(AssetId id, byte width) => SetBytes(id, [width]);
        private void SetBytes(AssetId id, byte[] bytes)
        {
            string locator = "asset-" + ++_revision;
            _locators[id] = locator;
            _store.Bytes[locator] = bytes;
        }
        private ContentManifest Manifest(bool includeCoin = true)
        {
            var entries = new List<ContentManifestEntry>();
            foreach (var pair in _locators)
            {
                if (!includeCoin && pair.Key == ContentRefs.Game_CoinRush_Coin.Id) continue;
                entries.Add(new ContentManifestEntry(pair.Key,
                    pair.Key == ContentRefs.Game_CoinRush_UiStrings.Id ? "raw-json" : "texture",
                    pair.Value, "settings", pair.Value, pair.Value, _store.Bytes[pair.Value].Length, []));
            }
            return new ContentManifest(1, entries.ToArray());
        }
        public async ValueTask DisposeAsync()
        {
            _reload.RequestStop();
            await Owner.DisposeAsync();
            await Bindings.DisposeAsync();
            await _reload.DisposeAsync();
            await _registry.DisposeAsync();
            _legacyAssets.Dispose();
            if (_fontRoot is not null) Directory.Delete(_fontRoot, recursive: true);
        }
    }

    private sealed class MemoryStore : IContentStore
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Bytes = new();
        public ReadOnlyMemory<byte> Get(string locator) => Bytes[locator];
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }

    private sealed class TexturePayloadLoader : IContentPayloadLoader
    {
        public bool CanLoad(Type type, string declared) => type == typeof(ITexture2D) && declared == "texture";
        public ValueTask<object> LoadAsync(Type type, string declared, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
            new(new TestTexture(bytes.Span[0]));
    }

    private sealed class TestTexture(byte width) : ITexture2D, IContentTrackedResource
    {
        public uint Width => width;
        public uint Height => 1;
        public ContentResourceLifetime Lifetime { get; } = new(() => { });
        public void Dispose() => throw new InvalidOperationException("Registry texture ownership must use its lifetime.");
    }

    private sealed class StubFontLoader : IAssetLoader
    {
        public string? DefaultPath => null;
        public string[] SupportedExtensions => [".font-json"];
        public Type AssetType => typeof(FontAsset);
        public JobHandle<Asset> LoadAsync(IAssetLoadContext context, Stream stream, string assetName) =>
            JobHandle<Asset>.FromResult(new FontAsset { Font = new StubFont() });
    }

    private sealed class StubFont : IFont
    {
        public ITexture2D AtlasTexture { get; } = new StubAtlas();
        public float Size => 16;
        public float LineHeight => 16;
        public float Ascender => 12;
        public float Descender => -4;
        public float DistanceRange => 4;
        public bool TryGetGlyph(uint codepoint, out FontGlyph glyph) { glyph = default; return false; }
        public void Dispose() => AtlasTexture.Dispose();
    }

    private sealed class StubAtlas : ITexture2D
    {
        public uint Width => 1;
        public uint Height => 1;
        public void Dispose() { }
    }

    private sealed class StubWindow : IWindow
    {
        public event Action Resized { add { } remove { } }
        public int Width => 1280;
        public int Height => 720;
        public string Title { get; set; } = "CoinRush presentation test";
        public NeoVeldrid.WindowState WindowState { get; set; }
        public bool Exists => true;
        public bool IsResized => false;
        public void Dispose() { }
    }
}

/// <summary>GraphicsContext has process-wide writer sets; this collection owns its tests exclusively.</summary>
[CollectionDefinition(nameof(PresentationGraphicsCollection), DisableParallelization = true)]
public sealed class PresentationGraphicsCollection;

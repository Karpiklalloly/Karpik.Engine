using System.Text.Json;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks source assets, metadata, and generated content manifests.</summary>
public sealed class ContentPipelineTests
{
    /// <summary>Verifies that unsupported match size and incomplete level fail fast.</summary>
    [Fact]
    public void Unsupported_match_size_and_incomplete_level_fail_fast()
    {
        Assert.Throws<InvalidDataException>(() =>
            CoinRushContent.ValidateMatch(CoinRushContent.DefaultMatch() with { MaxPlayers = 3 }));
        var level = JsonSerializer.Deserialize<LevelConfig>(File.ReadAllText(Content("CoinRush", "Level.json")))!;
        Assert.Throws<InvalidDataException>(() =>
            CoinRushContent.ValidateLevel(level with { PlayerSpawns = [level.PlayerSpawns[0]] }));
    }

    /// <summary>Finds the repository root from the solution file.</summary>
    /// <returns>The absolute repository root path.</returns>
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root (SSSuperGame.slnx) not found.");
    }

    /// <summary>Builds a path beneath the repository content directory.</summary>
    /// <param name="parts">The path segments beneath the content directory.</param>
    /// <returns>The absolute path to the requested content.</returns>
    private static string Content(params string[] parts)
    {
        return Path.Combine(new[] { RepoRoot(), "Content" }.Concat(parts).ToArray());
    }

    /// <summary>Verifies that CoinRush JSON config files deserialize into DTOs.</summary>
    [Fact]
    public void CoinRush_json_configs_parse_to_dtos()
    {
        var match = JsonSerializer.Deserialize<MatchConfig>(File.ReadAllText(Content("CoinRush", "Match.json")));
        Assert.NotNull(match);
        Assert.Equal(90f, match!.MatchDuration);
        Assert.Equal(2, match.MaxPlayers);
        Assert.Equal(14789, match.ServerPort);

        var level = JsonSerializer.Deserialize<LevelConfig>(File.ReadAllText(Content("CoinRush", "Level.json")));
        Assert.NotNull(level);
        Assert.Equal(2, level!.PlayerSpawns.Length);
        Assert.Equal(LevelData.Platforms.Length, level.Platforms.Length);
        Assert.Equal(LevelData.Crates.Length, level.Crates.Length);
        Assert.Equal(LevelData.Spikes.Length, level.Spikes.Length);

        var coins = JsonSerializer.Deserialize<CoinSpawnsConfig>(File.ReadAllText(Content("CoinRush", "CoinSpawns.json")));
        Assert.NotNull(coins);
        Assert.NotEmpty(coins!.Coins);

        var layers = JsonSerializer.Deserialize<PhysicsLayersDoc>(File.ReadAllText(Content("CoinRush", "PhysicsLayers.json")));
        Assert.NotNull(layers);
        Assert.Equal(CoinRushLayers.Player, layers!.Player);
        Assert.Equal(CoinRushLayers.PlayerMask, layers.PlayerMask);

        var ui = JsonSerializer.Deserialize<UiStrings>(File.ReadAllText(Content("CoinRush", "UiStrings.json")));
        Assert.NotNull(ui);
        Assert.Equal("COIN RUSH", ui!.Title);
    }

    /// <summary>Verifies that level JSON matches fallback geometry.</summary>
    [Fact]
    public void Level_json_matches_code_fallback_geometry()
    {
        var level = JsonSerializer.Deserialize<LevelConfig>(File.ReadAllText(Content("CoinRush", "Level.json")))!;
        for (int i = 0; i < LevelData.Platforms.Length; i++)
        {
            Assert.Equal(LevelData.Platforms[i].X, level.Platforms[i].X, precision: 4);
            Assert.Equal(LevelData.Platforms[i].Width, level.Platforms[i].Width, precision: 4);
        }
        for (int i = 0; i < LevelData.Coins.Length; i++)
        {
            var coins = JsonSerializer.Deserialize<CoinSpawnsConfig>(File.ReadAllText(Content("CoinRush", "CoinSpawns.json")))!;
            Assert.Equal(LevelData.Coins[i].X, coins.Coins[i].X, precision: 4);
        }
    }

    /// <summary>Verifies that each asset metadata file has the required contract.</summary>
    [Fact]
    public void Every_meta_has_stable_contract()
    {
        string[] metas = Directory.GetFiles(Content(), "*.meta", SearchOption.AllDirectories);
        Assert.True(metas.Length >= 13, $"Expected at least 13 metas, found {metas.Length}.");
        var allowed = new HashSet<string> { "raw-json", "texture", "font-json", "shader" };
        foreach (string metaPath in metas)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            JsonElement root = doc.RootElement;
            Assert.True(Guid.TryParse(root.GetProperty("assetId").GetString(), out _), metaPath);
            string logical = root.GetProperty("logicalName").GetString()!;
            Assert.StartsWith("game/", logical);
            Assert.Contains(root.GetProperty("declaredType").GetString()!, allowed);
            foreach (JsonElement target in root.GetProperty("targets").EnumerateArray())
            {
                Assert.Contains(target.GetString()!, new[] { "Client", "Server", "Shared" });
            }
            string source = metaPath.Substring(0, metaPath.Length - ".meta".Length);
            Assert.True(File.Exists(source), $"Missing source for {metaPath}.");
        }
    }

    /// <summary>Verifies unique asset IDs and logical names in metadata.</summary>
    [Fact]
    public void Meta_assetIds_are_unique_and_immutable_shape()
    {
        string[] metas = Directory.GetFiles(Content(), "*.meta", SearchOption.AllDirectories);
        var ids = new HashSet<string>();
        var logicals = new HashSet<string>();
        foreach (string metaPath in metas)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            string id = doc.RootElement.GetProperty("assetId").GetString()!;
            string logical = doc.RootElement.GetProperty("logicalName").GetString()!;
            Assert.True(ids.Add(id), $"Duplicate assetId {id}.");
            Assert.True(logicals.Add(logical), $"Duplicate logicalName {logical}.");
        }
    }

    /// <summary>Verifies that every required source asset type is present.</summary>
    [Fact]
    public void Mixed_types_present()
    {
        string root = Content();
        Assert.True(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length >= 6);
        Assert.True(Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Length >= 3);
        Assert.True(Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories).Length >= 1);
        Assert.True(Directory.GetFiles(root, "*.font-json", SearchOption.AllDirectories).Length >= 1);
        Assert.True(Directory.GetFiles(root, "*.vert", SearchOption.AllDirectories).Length >= 1);
        Assert.True(Directory.GetFiles(root, "*.frag", SearchOption.AllDirectories).Length >= 2);
    }

    /// <summary>Verifies that the legacy player template has no hardcoded texture path.</summary>
    [Fact]
    public void Legacy_player_template_has_no_hardcoded_texture_path()
    {
        string json = File.ReadAllText(Content("Player.json"));

        Assert.DoesNotContain("TexturePath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SpriteRenderer", json, StringComparison.Ordinal);
    }

    /// <summary>Verifies that the HUD separates its title and match phase.</summary>
    [Fact]
    public void Hud_does_not_combine_title_and_phase_into_one_overwide_line()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "Source", "SSSuperGame.Client", "CoinRush", "ClientDrawSystem.cs"));

        Assert.DoesNotContain("_ui.Title + \"   \" + phase", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies PNG file signatures.</summary>
    [Fact]
    public void Png_sources_have_png_magic()
    {
        foreach (string png in Directory.GetFiles(Content(), "*.png", SearchOption.AllDirectories))
        {
            byte[] head = new byte[8];
            using (FileStream fs = File.OpenRead(png))
            {
                Assert.Equal(8, fs.Read(head, 0, 8));
            }
            byte[] magic = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Assert.Equal(magic, head);
        }
    }

    /// <summary>Verifies required font JSON fields and shader entry points.</summary>
    [Fact]
    public void Font_json_and_shaders_are_valid_sources()
    {
        foreach (string font in Directory.GetFiles(Content(), "*.font-json", SearchOption.AllDirectories))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(font));
            Assert.True(doc.RootElement.TryGetProperty("atlas", out _), font);
            Assert.True(doc.RootElement.TryGetProperty("glyphs", out _), font);
        }
        foreach (string shader in Directory.GetFiles(Content(), "*.vert", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(Content(), "*.frag", SearchOption.AllDirectories)))
        {
            string text = File.ReadAllText(shader);
            Assert.Contains("void main", text);
        }
    }

    /// <summary>Verifies that the built manifest contains all required logical asset names.</summary>
    [Fact]
    public void Built_manifest_contains_all_logical_names()
    {
        string manifest = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Client", "obj", "Debug", "net10.0", "Content", "manifest.json");
        Assert.True(File.Exists(manifest), "Build the Client project first (content build emits the manifest).");
        string json = File.ReadAllText(manifest);
        foreach (string logical in new[] { "game/CoinRush/Match", "game/CoinRush/Level", "game/CoinRush/CoinSpawns", "game/CoinRush/PhysicsLayers", "game/CoinRush/UiStrings", "game/CoinRush/Coin", "game/CoinRush/Tile" })
        {
            Assert.Contains(logical, json);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>AOT-safe UI metadata owned by the client presentation simulation.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(UiStrings))]
public partial class ClientPresentationJsonContext : JsonSerializerContext
{
    /// <summary>Builds owner-scoped source metadata with cold UI validation before publication.</summary>
    public static JsonTypeInfo<UiStrings> CreateUiTypeInfo()
    {
        var context = new ClientPresentationJsonContext();
        var options = new JsonSerializerOptions(context.Options)
        {
            TypeInfoResolver = context.WithAddedModifier(static info =>
            {
                if (info.Type == typeof(UiStrings))
                    info.OnDeserialized = static value => Validate((UiStrings)value);
            })
        };
        options.MakeReadOnly();
        return (JsonTypeInfo<UiStrings>)options.GetTypeInfo(typeof(UiStrings));
    }

    private static void Validate(UiStrings ui)
    {
        ValidateField(ui.Title); ValidateField(ui.Waiting); ValidateField(ui.Countdown);
        ValidateField(ui.Finished); ValidateField(ui.Draw); ValidateField(ui.Player0);
        ValidateField(ui.Player1); ValidateField(ui.ControlsHint); ValidateField(ui.RestartHint);
        ValidateField(ui.NewRoundHint);
        int longestPlayer = Math.Max(ui.Player0.Length, ui.Player1.Length);
        if (longestPlayer + 12 > ClientHudText.MaximumFormattedLength ||
            ui.Player0.Length + ui.Player1.Length + 32 > ClientHudText.MaximumFormattedLength ||
            ui.Title.Length + longestPlayer + 1 > ClientHudText.MaximumFormattedLength ||
            ui.Countdown.Length + 33 > ClientHudText.MaximumFormattedLength)
            throw new JsonException("Combined CoinRush UI labels exceed the fixed HUD text capacity.");

        // Setup and sequential RenderPrepare may execute on different threads.
        // A fresh simulation ThreadBuffer owns 4096 copied characters. Keep
        // every valid UI within that default budget, so a live localization
        // change cannot grow a render-thread buffer after warm-up.
        int playerLabel = longestPlayer + 12; // space + signed Int32
        int score = ui.Player0.Length + ui.Player1.Length + 27; // two signed Int32s + separators
        int phase = Math.Max(Math.Max(ui.Waiting.Length, ui.Draw.Length),
            Math.Max(Math.Max(ui.Countdown.Length + 33, 34), ui.Title.Length + longestPlayer + 1));
        int hints = ui.ControlsHint.Length + Math.Max(ui.RestartHint.Length, ui.NewRoundHint.Length);
        // Only player labels, phase and score currently use CopyText. Include
        // the title, hints and one end banner conservatively as well.
        int frameText = (playerLabel * 2) + score + phase + ui.Title.Length + hints + ui.Finished.Length;
        if (frameText > 4096)
            throw new JsonException("CoinRush UI exceeds the fixed 4096-character presentation frame budget.");
    }

    private static void ValidateField(string? value)
    {
        if (value is null || value.Length > ClientHudText.MaximumFormattedLength)
            throw new JsonException("CoinRush UI strings must be non-null and fit the fixed HUD text capacity.");
    }
}

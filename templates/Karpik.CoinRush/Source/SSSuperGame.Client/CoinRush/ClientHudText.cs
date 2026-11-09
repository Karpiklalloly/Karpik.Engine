using System.Globalization;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Formats dynamic HUD text into caller-owned buffers without frame allocations.</summary>
public static class ClientHudText
{
    /// <summary>Bounded stack storage for combined labels; oversized localization fails explicitly.</summary>
    public const int MaximumFormattedLength = 2048;

    public static bool TryPlayerLabel(Span<char> output, string name, int score, out int written)
    {
        int count = 0;
        bool success = Append(output, ref count, name) && Append(output, ref count, " ") &&
            AppendInt(output, ref count, score);
        written = success ? count : 0;
        return success;
    }

    public static bool TryScore(Span<char> output, UiStrings ui, in MatchState state, out int written)
    {
        int count = 0;
        bool success = Append(output, ref count, ui.Player0) && Append(output, ref count, " ") &&
            AppendInt(output, ref count, state.Score0) && Append(output, ref count, " : ") &&
            AppendInt(output, ref count, state.Score1) && Append(output, ref count, " ") &&
            Append(output, ref count, ui.Player1);
        written = success ? count : 0;
        return success;
    }

    public static bool TryPhase(Span<char> output, UiStrings ui, in MatchState state, out int written)
    {
        int count = 0;
        bool success;
        switch (state.Phase)
        {
            case MatchPhase.WaitingForPlayers:
                return TryCopy(output, ui.Waiting, out written);
            case MatchPhase.Countdown:
                success = Append(output, ref count, ui.Countdown) && Append(output, ref count, " ") &&
                    AppendFloat(output, ref count, MathF.Ceiling(state.TimeLeft));
                break;
            case MatchPhase.Running:
                success = Append(output, ref count, "⏱ ") &&
                    AppendFloat(output, ref count, MathF.Ceiling(state.TimeLeft));
                break;
            case MatchPhase.Finished when state.Winner < 0:
                return TryCopy(output, ui.Draw, out written);
            case MatchPhase.Finished:
                success = Append(output, ref count, ui.Title) && Append(output, ref count, " ") &&
                    Append(output, ref count, state.Winner == 0 ? ui.Player0 : ui.Player1);
                break;
            default:
                written = 0;
                return true;
        }
        written = success ? count : 0;
        return success;
    }

    private static bool Append(Span<char> output, ref int count, ReadOnlySpan<char> value)
    {
        if (!value.TryCopyTo(output[count..])) return false;
        count += value.Length;
        return true;
    }

    // Concrete TryFormat calls avoid the interpolated handler's value-type
    // interface checks/boxing in unoptimized Debug client builds.
    private static bool AppendInt(Span<char> output, ref int count, int value)
    {
        if (!value.TryFormat(output[count..], out int length, provider: CultureInfo.InvariantCulture)) return false;
        count += length;
        return true;
    }

    private static bool AppendFloat(Span<char> output, ref int count, float value)
    {
        if (!value.TryFormat(output[count..], out int length, provider: CultureInfo.InvariantCulture)) return false;
        count += length;
        return true;
    }

    private static bool TryCopy(Span<char> output, string value, out int written)
    {
        if (value.AsSpan().TryCopyTo(output)) { written = value.Length; return true; }
        written = 0;
        return false;
    }
}

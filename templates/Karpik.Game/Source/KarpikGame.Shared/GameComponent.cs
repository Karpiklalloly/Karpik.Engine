using DCFApixels.DragonECS;

namespace KarpikGame.Shared;

public struct GameComponent : IEcsComponent
{
    public int Value;

    // Editor snapshot display falls back to ToString when trimmed field
    // metadata is unavailable (NativeAOT), so the value must survive there.
    public override string ToString() => $"{nameof(Value)} = {Value}";
}

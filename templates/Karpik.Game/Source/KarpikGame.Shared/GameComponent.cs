using DCFApixels.DragonECS;

namespace KarpikGame.Shared;

public struct GameComponent : IEcsComponent
{
    public int Value;

    public override string ToString() => $"{nameof(Value)} = {Value}";
}

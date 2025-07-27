using DCFApixels.DragonECS;

namespace Karpik.Game.Modules;

[Serializable]
public struct FollowTarget : IEcsComponent
{
    public int Target;
}

[Serializable]
public struct FollowPlayer : IEcsTagComponent;

[Serializable]
public struct Player : IEcsTagComponent;

public struct PlayerRef : IEcsComponent
{
    public entlong Player;
}

[Serializable]
public struct Enemy : IEcsTagComponent;
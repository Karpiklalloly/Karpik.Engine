using System.Numerics;
using System.Runtime.CompilerServices;
using Karpik.Engine.Shared;

namespace Karpik.Engine.Server.Movement;

public static class MovementExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MoveBySpeed(this entlong entity, Vector2 direction)
    {
        var speed = entity.Get<Speed>().Value;
        var body = entity.Get<RigidBody>();
        entity.Move((float)(speed * body.Mass) * direction.Normalized() * (float)Time.FixedDeltaTime);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move(this entlong entity, Vector2 direction)
    {
        // var world = entity.World;
        // ref var velocity = ref world.GetPool<Force>().TryAddOrGet(entity.ID);
        // velocity.Direction += direction;
    }
}
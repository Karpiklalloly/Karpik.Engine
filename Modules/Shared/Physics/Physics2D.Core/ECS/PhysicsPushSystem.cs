using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsPushSystem : ISystemBegin
{
    class TeleportAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<TeleportRequest> Teleports = Inc;
    }

    class VelocityAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<SetVelocityRequest> VelocityRequests = Inc;
    }

    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private DefaultWorld _world = null!;
    
    private PhysicsBodyHandle[] _handlesBuf = new PhysicsBodyHandle[1024];
    private Vector2[] _vecBuf = new Vector2[1024];
    private float[] _floatBuf = new float[1024];
    
    public void Begin()
    {
        ProcessTeleports();
        ProcessVelocities();
    }
    
    private void ProcessTeleports()
    {
        var span = _world.Where(out TeleportAspect aspect);
        int count = span.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        for (int i = 0; i < count; i++) 
        {
            int entity = span[i];
            _handlesBuf[i] = aspect.Bodies.Get(entity).Handle;
            
            ref var req = ref aspect.Teleports.Get(entity);
            _vecBuf[i] = req.Position;
            _floatBuf[i] = req.Rotation;

            aspect.Teleports.Del(entity);
        }

        _physics.SetTransforms(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _floatBuf.AsSpan(0, count)
        );
    }
    
    private void ProcessVelocities()
    {
        var query = _world.Where(out VelocityAspect aspect);
        int count = query.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        for (int i = 0; i < count; i++) 
        {
            int entity = query[i];
            _handlesBuf[i] = aspect.Bodies.Get(entity).Handle;
            
            ref var req = ref aspect.VelocityRequests.Get(entity);
            _vecBuf[i] = req.Linear;
            _floatBuf[i] = req.Angular;

            aspect.VelocityRequests.Del(entity);
        }

        _physics.SetVelocities(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _floatBuf.AsSpan(0, count)
        );
    }
    
    private void EnsureCapacity(int requiredCount)
    {
        if (_handlesBuf.Length < requiredCount)
        {
            int newSize = Math.Max(_handlesBuf.Length * 2, requiredCount);
            Array.Resize(ref _handlesBuf, newSize);
            Array.Resize(ref _vecBuf, newSize);
            Array.Resize(ref _floatBuf, newSize);
        }
    }
}
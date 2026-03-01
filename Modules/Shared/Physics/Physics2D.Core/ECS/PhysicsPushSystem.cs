using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsPushSystem : IEcsRun
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

    [DI] private IPhysicsWorld2D _physics;
    [DI] private EcsDefaultWorld _world;
    
    private PhysicsBodyHandle[] _handlesBuf = new PhysicsBodyHandle[1024];
    private Vector2[] _vecBuf = new Vector2[1024];
    private float[] _floatBuf = new float[1024];
    
    public void Run()
    {
        ProcessTeleports();
        ProcessVelocities();
    }
    
    private void ProcessTeleports()
    {
        var query = _world.Where(out TeleportAspect aspect);
        int count = query.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        for (int i = 0; i < count; i++) 
        {
            int entity = query[i];
            _handlesBuf[i] = aspect.Bodies.Get(entity).Handle;
            
            ref var req = ref aspect.Teleports.Get(entity);
            _vecBuf[i] = req.Position;
            _floatBuf[i] = req.Rotation;

            // Удаляем запрос, чтобы не телепортировать в следующем кадре
            aspect.Teleports.Del(entity);
        }

        // Передаем пачку данных в C++ / Aether2D
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

            // Удаляем запрос
            aspect.VelocityRequests.Del(entity);
        }

        // Передаем пачку скоростей в физику
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
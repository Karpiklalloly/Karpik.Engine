using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Spatial2D;
using OpenTK.Mathematics;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsPullSystem(IPhysicsWorld2D physics, DefaultWorld world) : ISystemLateUpdate
{
    class TransformAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<Transform2D> Transforms = Inc;
    }

    class VelocityAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<Velocity2D> Velocities = Inc;
    }

    private PhysicsBodyHandle[] _handlesBuf = new PhysicsBodyHandle[2048];
    private Vector2d[] _vecBuf = new Vector2d[2048];
    private float[] _floatBuf = new float[2048];
    private double[] _doubleBuf = new double[2048];

    public void LateUpdate()
    {
        SyncTransforms();
        SyncVelocities();
    }
    
     private void SyncTransforms()
    {
        var span = world.Where(out TransformAspect aspect);
        int count = span.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        for (int i = 0; i < count; i++) 
        {
            _handlesBuf[i] = aspect.Bodies.Get(span[i]).Handle;
        }

        physics.GetTransforms(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _floatBuf.AsSpan(0, count)
        );

        for (int i = 0; i < count; i++) 
        {
            ref var transform = ref aspect.Transforms.Get(span[i]);
            transform.Position = _vecBuf[i];
            transform.Rotation = _floatBuf[i];
        }
    }

    private void SyncVelocities()
    {
        var span = world.Where(out VelocityAspect aspect);
        int count = span.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        for (int i = 0; i < count; i++) 
        {
            _handlesBuf[i] = aspect.Bodies.Get(span[i]).Handle;
        }

        physics.GetVelocities(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _doubleBuf.AsSpan(0, count)
        );

        for (int i = 0; i < count; i++) 
        {
            ref var velocity = ref aspect.Velocities.Get(span[i]);
            velocity.Linear = _vecBuf[i];
            velocity.Angular = _doubleBuf[i];
        }
    }

    private void EnsureCapacity(int requiredCount)
    {
        if (_handlesBuf.Length < requiredCount)
        {
            int newSize = Math.Max(_handlesBuf.Length * 2, requiredCount);
            Array.Resize(ref _handlesBuf, newSize);
            Array.Resize(ref _vecBuf, newSize);
            Array.Resize(ref _floatBuf, newSize);
            Array.Resize(ref _doubleBuf, newSize);
        }
    }
}
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsPullSystem : IEcsRun
{
    // Аспект 1: Тела, которым нужно обновлять позицию и вращение
    class TransformAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<Transform2D> Transforms = Inc;
    }

    // Аспект 2: Тела, которым нужно обновлять скорость (например, персонаж, ракеты)
    class VelocityAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> Bodies = Inc;
        public EcsPool<Velocity2D> Velocities = Inc;
    }
    
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private EcsDefaultWorld _world = null!;
    
    private PhysicsBodyHandle[] _handlesBuf = new PhysicsBodyHandle[2048];
    private Vector2[] _vecBuf = new Vector2[2048];
    private float[] _floatBuf = new float[2048];
    
    public void Run()
    {
        SyncTransforms();
        SyncVelocities();
    }
    
     private void SyncTransforms()
    {
        var query = _world.Where(out TransformAspect aspect);
        int count = query.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        // 1. Собираем Handle
        // Линейный проход по памяти ECS. Предсказатель переходов ликует.
        for (int i = 0; i < count; i++) 
        {
            _handlesBuf[i] = aspect.Bodies.Get(query[i]).Handle;
        }

        // 2. Делаем bulk-вызов в Aether2D (1 виртуальный вызов вместо тысяч!)
        _physics.GetTransforms(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _floatBuf.AsSpan(0, count)
        );

        // 3. Записываем результаты обратно в компоненты
        for (int i = 0; i < count; i++) 
        {
            ref var transform = ref aspect.Transforms.Get(query[i]);
            transform.Position = _vecBuf[i];
            transform.Rotation = _floatBuf[i];
        }
    }

    private void SyncVelocities()
    {
        var query = _world.Where(out VelocityAspect aspect);
        int count = query.Count;
        if (count == 0) return;

        EnsureCapacity(count);

        // 1. Собираем Handle
        for (int i = 0; i < count; i++) 
        {
            _handlesBuf[i] = aspect.Bodies.Get(query[i]).Handle;
        }

        // 2. Bulk-вызов в Aether2D за скоростями
        _physics.GetVelocities(
            _handlesBuf.AsSpan(0, count), 
            _vecBuf.AsSpan(0, count), 
            _floatBuf.AsSpan(0, count)
        );

        // 3. Записываем скорости обратно в компоненты ECS
        for (int i = 0; i < count; i++) 
        {
            ref var velocity = ref aspect.Velocities.Get(query[i]);
            velocity.Linear = _vecBuf[i];
            velocity.Angular = _floatBuf[i];
        }
    }

    // Динамический ресайз массивов без потери данных 
    // (Вызовется пару раз за игру при спавне большого числа объектов, потом перестанет)
    private void EnsureCapacity(int requiredCount)
    {
        if (_handlesBuf.Length < requiredCount)
        {
            // Увеличиваем с запасом (x2), чтобы не ресайзить каждый кадр
            int newSize = Math.Max(_handlesBuf.Length * 2, requiredCount);
            Array.Resize(ref _handlesBuf, newSize);
            Array.Resize(ref _vecBuf, newSize);
            Array.Resize(ref _floatBuf, newSize);
        }
    }
}
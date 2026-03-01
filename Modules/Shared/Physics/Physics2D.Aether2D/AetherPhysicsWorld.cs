using Karpik.Engine.Shared.Physics.Core;
using nkast.Aether.Physics2D.Collision;
using nkast.Aether.Physics2D.Collision.Shapes;
using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using ShapeType = Karpik.Engine.Shared.Physics.Core.ShapeType;
using Vector2 = System.Numerics.Vector2;

namespace Karpik.Engine.Shared.Physics.Aether2D;

public sealed class AetherPhysicsWorld : IPhysicsWorld2D 
{
    private class BodyLink 
    {
        public int EntityId;
        public PhysicsBodyHandle Handle;
    }
    
    private readonly World _world;
    private readonly RayCastReportFixtureDelegate _cachedRaycastDelegate;
    private readonly QueryReportFixtureDelegate _cachedOverlapDelegate;

    private Body[] _bodies = new Body[4096];
    private int[] _freeHandles = new int[4096];
    private int _freeHandleCount = 0;
    private int _nextHandleId = 0;
    
    private BodyLink[] _linkPool = new BodyLink[4096];
    private int _linkPoolCount = 0;

    private CollisionEvent[] _collisionBuffer = new CollisionEvent[1024];
    private int _collisionCount = 0;

    private readonly OnCollisionEventHandler _cachedCollisionHandler;

    // Поля для передачи состояния в рейкаст-коллбек без замыканий (closures)
    private unsafe RaycastHit2D* _currentRaycastBufferPtr;
    private int _currentRaycastBufferLength;
    private int _currentRaycastHits;
    private PhysicsLayerMask _currentRaycastMask;

    private unsafe RaycastHit2D* _currentOverlapBufferPtr;
    private int _currentOverlapBufferLength;
    private int _currentOverlapCount;
    private PhysicsLayerMask _currentOverlapMask;
    
    private readonly CircleShape _cachedQueryCircle = new CircleShape(1f, 1f);
    private Transform _cachedQueryTransform = new Transform();

    public AetherPhysicsWorld()
    {
        _world = new World(new Vector2(0, -9.8f).Aether);
        _cachedCollisionHandler = OnBodyCollision;
        _cachedRaycastDelegate = AetherRaycastCallback;
        _cachedOverlapDelegate = AetherOverlapCallback;
    }

    public void Step(float deltaTime)
    {
        _collisionCount = 0;
        _world.Step(deltaTime);
    }

    public PhysicsBodyHandle CreateBody(int entityId, Vector2 position, float rotation, in BodyConfig bodyCfg, in ShapeConfig shapeCfg)
    {
        // Создаем тело в Aether2D
        var aetherBodyType = bodyCfg.Type.Aether;

        Body body = _world.CreateBody(position.Aether, rotation, aetherBodyType);
        
        // Настраиваем форму
        Fixture fixture = shapeCfg.Type switch
        {
            ShapeType.Box => body.CreateRectangle(shapeCfg.BoxSize.X, shapeCfg.BoxSize.Y, 1f, Vector2.Zero.Aether),
            ShapeType.Circle => body.CreateCircle(shapeCfg.CircleRadius, 1f, Vector2.Zero.Aether),
            _ => throw new ArgumentOutOfRangeException(nameof(shapeCfg.Type), shapeCfg.Type, null)
        };

        if (fixture != null) {
            fixture.Friction = bodyCfg.Friction;
            fixture.Restitution = bodyCfg.Restitution;
            fixture.IsSensor = bodyCfg.IsSensor;
            fixture.CollisionCategories = (Category)bodyCfg.CategoryBits;
            fixture.CollidesWith = (Category)bodyCfg.MaskBits;
        }

        body.Mass = bodyCfg.Mass;
        
        // Подписываемся на коллизии без выделения памяти (используем закешированный делегат)
        body.OnCollision += _cachedCollisionHandler;

        // Выделяем Handle из FreeList (O(1))
        int handleId;
        if (_freeHandleCount > 0) {
            handleId = _freeHandles[--_freeHandleCount];
        } else {
            handleId = _nextHandleId++;
            if (handleId >= _bodies.Length) {
                Array.Resize(ref _bodies, _bodies.Length * 2);
                Array.Resize(ref _freeHandles, _freeHandles.Length * 2);
            }
        }
        
        var handle = new PhysicsBodyHandle(handleId);
        body.Tag = GetLink(entityId, handle);

        _bodies[handleId] = body;
        return handle;
    }

    public void DestroyBody(PhysicsBodyHandle handle)
    {
        var body = _bodies[handle.Value];
        if (body == null) return;
        if (body.Tag is BodyLink link) ReturnLink(link);

        body.OnCollision -= _cachedCollisionHandler;
        _world.Remove(body);
        
        // Очищаем ссылку для GC и возвращаем Handle в пул
        _bodies[handle.Value] = null;
        _freeHandles[_freeHandleCount++] = handle.Value;
    }

    public void GetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outPositions, Span<float> outRotations)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            var body = _bodies[handles[i].Value];
            outPositions[i] = new Vector2(body.Position.X, body.Position.Y);
            outRotations[i] = body.Rotation;
        }
    }
    
    public void GetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outLinear, Span<float> outAngular)
    {
        // Максимально плотный цикл. Без проверок на null для скорости.
        for (int i = 0; i < handles.Length; i++)
        {
            var body = _bodies[handles[i].Value];
            outLinear[i] = new Vector2(body.LinearVelocity.X, body.LinearVelocity.Y);
            outAngular[i] = body.AngularVelocity;
        }
    }
    
    public void SetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2> positions, ReadOnlySpan<float> rotations)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            var body = _bodies[handles[i].Value];
            // SetTransform в Aether2D автоматически обновляет BroadPhase (AABB)
            body.SetTransform(new Vector2(positions[i].X, positions[i].Y).Aether, rotations[i]);
        }
    }
    
    public void SetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2> linear, ReadOnlySpan<float> angular)
    {
        // Максимально плотный C-style цикл
        for (int i = 0; i < handles.Length; i++)
        {
            var body = _bodies[handles[i].Value];
            body.LinearVelocity = new Vector2(linear[i].X, linear[i].Y).Aether;
            body.AngularVelocity = angular[i];
        }
    }

    public unsafe int Raycast(Vector2 start, Vector2 end, PhysicsLayerMask layerMask, Span<RaycastHit2D> results)
    {
        if (results.Length == 0) return 0;

        _currentRaycastHits = 0;
        _currentRaycastMask = layerMask;
        _currentRaycastBufferLength = results.Length;

        // fixed "прибивает" массив/память, чтобы сборщик мусора его не сдвинул
        fixed (RaycastHit2D* ptr = results)
        {
            _currentRaycastBufferPtr = ptr;
                
            // Используем закешированный делегат! Никаких замыканий.
            _world.RayCast(_cachedRaycastDelegate, start.Aether, end.Aether);
                
            _currentRaycastBufferPtr = null; // Безопасность прежде всего
        }
            
        return _currentRaycastHits;
    }

    private unsafe float AetherRaycastCallback(Fixture fixture, nkast.Aether.Physics2D.Common.Vector2 point, nkast.Aether.Physics2D.Common.Vector2 normal, float fraction)
    {
        if (_currentRaycastHits >= _currentRaycastBufferLength)
            return 0; // Буфер переполнен, прерываем луч Aether

        if (((uint)fixture.CollisionCategories & _currentRaycastMask.Value) == 0)
            return -1; // Игнорируем маску

        var link = fixture.Body.Tag as BodyLink;

        // Пишем напрямую в память через указатель! Максимальная производительность.
        _currentRaycastBufferPtr[_currentRaycastHits++] = new RaycastHit2D
        {
            Entity = link?.EntityId ?? -1,
            Body = link?.Handle ?? PhysicsBodyHandle.Invalid,
            Point = new Vector2(point.X, point.Y),
            Normal = new Vector2(normal.X, normal.Y),
            Fraction = fraction
        };

        return 1;
    }

    // ==========================================
    // СОБЫТИЯ КОЛЛИЗИЙ (BUFFERED)
    // ==========================================
    public ReadOnlySpan<CollisionEvent> GetFrameCollisions()
    {
        return _collisionBuffer.AsSpan(0, _collisionCount);
    }

    public void ApplyForce(PhysicsBodyHandle handle, Vector2 force, Vector2 point)
    {
        var body = _bodies[handle.Value];
        body.ApplyForce(
            new Vector2(force.X, force.Y).Aether, 
            new Vector2(point.X, point.Y).Aether
        );
    }

    public void ApplyLinearImpulse(PhysicsBodyHandle handle, Vector2 impulse)
    {
        var body = _bodies[handle.Value];
        body.ApplyLinearImpulse(new Vector2(impulse.X, impulse.Y).Aether);
    }

    public float GetMass(PhysicsBodyHandle handle)
    {
        return _bodies[handle.Value].Mass;
    }
    
    public unsafe int OverlapCircle(Vector2 center, float radius, PhysicsLayerMask layerMask, Span<RaycastHit2D> results)
    {
        if (results.Length == 0) return 0;

        _currentOverlapCount = 0;
        _currentOverlapMask = layerMask;
        _currentOverlapBufferLength = results.Length;

        // Настраиваем фигуру
        _cachedQueryCircle.Radius = radius;
        _cachedQueryTransform.p = new Vector2(center.X, center.Y).Aether;
            
        // FIX: Вращение в Aether2D (Complex). Угол 0 градусов = Cos(0)=1, Sin(0)=0.
        _cachedQueryTransform.q = new Complex(1f, 0f);

        AABB aabb;
        aabb.LowerBound = new Vector2(center.X - radius, center.Y - radius).Aether;
        aabb.UpperBound = new Vector2(center.X + radius, center.Y + radius).Aether;

        fixed (RaycastHit2D* ptr = results)
        {
            _currentOverlapBufferPtr = ptr;
            _world.QueryAABB(_cachedOverlapDelegate, ref aabb);
            _currentOverlapBufferPtr = null;
        }

        return _currentOverlapCount;
    }
    
    private unsafe bool AetherOverlapCallback(Fixture fixture)
    {
        if (_currentOverlapCount >= _currentOverlapBufferLength)
            return false;

        if (((uint)fixture.CollisionCategories & _currentOverlapMask.Value) == 0)
            return true;

        fixture.Body.GetTransform(out Transform fixtureTransform);
            
        bool isOverlapping = Collision.TestOverlap(
            _cachedQueryCircle, 0, 
            fixture.Shape, 0, 
            ref _cachedQueryTransform, ref fixtureTransform
        );

        if (isOverlapping)
        {
            var link = fixture.Body.Tag as BodyLink;

            _currentOverlapBufferPtr[_currentOverlapCount++] = new RaycastHit2D
            {
                Entity = link?.EntityId ?? -1,
                Body = link?.Handle ?? PhysicsBodyHandle.Invalid,
                Point = new Vector2(fixtureTransform.p.X, fixtureTransform.p.Y),
                Normal = Vector2.Zero,
                Fraction = 0f
            };
        }

        return true;
    }
    
    private bool OnBodyCollision(Fixture sender, Fixture other, Contact contact)
    {
        if (_collisionCount >= _collisionBuffer.Length)
            return true; // Игнорируем запись, если буфер переполнен, но физике разрешаем коллизию

        int entityA = sender.Body.Tag is int idA ? idA : -1;
        int entityB = other.Body.Tag is int idB ? idB : -1;

        // Aether2D Contact имеет сложную структуру, достаем нормаль
        contact.GetWorldManifold(out var manifoldNormal, out _);

        _collisionBuffer[_collisionCount++] = new CollisionEvent
        {
            EntityA = entityA,
            EntityB = entityB,
            Normal = new Vector2(manifoldNormal.X, manifoldNormal.Y),
            // В Aether импульс рассчитывается в PostSolve, но мы можем сохранить базовые данные
            Impulse = 0 
        };

        return true; // true означает, что тела отскочат друг от друга
    }
    
    private BodyLink GetLink(int entityId, PhysicsBodyHandle handle) 
    {
        var link = _linkPoolCount > 0 ? _linkPool[--_linkPoolCount] : new BodyLink();
        link.EntityId = entityId;
        link.Handle = handle;
        return link;
    }

    private void ReturnLink(BodyLink link) 
    {
        if (_linkPoolCount < _linkPool.Length)
            _linkPool[_linkPoolCount++] = link;
    }
}
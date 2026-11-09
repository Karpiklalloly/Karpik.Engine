using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyDestroyer(IPhysicsWorld2D physics, DefaultWorld world)
    : ISystemInit, ISystemLateUpdate, ISystemDestroy, IEcsEntityEventListener
{
    private EcsPool<PhysicsBodyRef> _bodyRefs = null!;
    private bool _isListening;

    class Aspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> BodyRefs = Inc;
        public EcsPool<DestroyBodyRequest> Requests = Inc;
    }

    public void Init()
    {
        if (_isListening) return;
        _bodyRefs = world.Base.GetPool<PhysicsBodyRef>();
        world.Base.AddListener(this);
        _isListening = true;
    }

    public void Destroy()
    {
        if (!_isListening) return;
        world.Base.RemoveListener(this);
        _isListening = false;
    }

    public void OnNewEntity(int entityID) { }
    public void OnMigrateEntity(int entityID) { }

    public void OnDelEntity(int entityID)
    {
        // Dragon marks the entity dead before deferring component removal. Read
        // the handle now; a later aspect query cannot discover the deleted entity.
        if (_bodyRefs.Has(entityID)) DestroyBody(_bodyRefs, entityID);
    }

    public void LateUpdate()
    {
        foreach (var e in world.Where(out Aspect destroy))
        {
            DestroyBody(destroy.BodyRefs, e);
            
            destroy.BodyRefs.Del(e);
            destroy.Requests.Del(e);
        }
    }

    private void DestroyBody(EcsPool<PhysicsBodyRef> bodyRefs, int entityID)
    {
        var handle = bodyRefs.Read(entityID).Handle;
        if (!handle.IsValid) return;
        // Invalidate before releasing the backend handle, which may be reused.
        bodyRefs.Get(entityID).Handle = PhysicsBodyHandle.Invalid;
        physics.DestroyBody(handle);
    }
}

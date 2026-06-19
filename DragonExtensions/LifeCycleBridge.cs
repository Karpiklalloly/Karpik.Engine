using Karpik.Engine.Core;

namespace DragonExtensions;

public class InitSystem(ISystemInit system) : IEcsInit, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;
    
    public void Init()
    {
        system.Init();
    }

    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class BeginSystem(ISystemBegin system) : IBeginRunSystem, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public void BeginRun()
    {
        system.Begin();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class FixedUpdateSystem(ISystemFixedUpdate system) : IEcsFixedRun, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public void FixedRun()
    {
        system.FixedUpdate();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class UpdateSystem(ISystemUpdate system) : IUpdateSystem, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public ISystemUpdate System => system;

    public void Update()
    {
        system.Update();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class LateSystem(ISystemLateUpdate system) : ILateRunSystem, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public void LateRun()
    {
        system.LateUpdate();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class RenderSystem(ISystemRender system) : IRenderSystem, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public void Render()
    {
        system.Render();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

public class DestroySystem(ISystemDestroy system) : IEcsDestroy, IOnInjectedDI
{
    [DI] private IServiceContainer _container = null!;

    public void Destroy()
    {
        system.Destroy();
    }
    
    public void OnInjected()
    {
        Injector injector = _container.Get<Injector>()!;
        _container.Inject(system);
        injector.Inject(system);
    }
}

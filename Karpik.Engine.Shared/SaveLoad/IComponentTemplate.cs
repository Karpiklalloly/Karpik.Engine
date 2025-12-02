using System.Collections.Concurrent;
using System.Reflection;

namespace Karpik.Engine.Shared;

public interface IComponentTemplate
{
    public Type Type { get; }
    public bool IsUnique { get; }

    public object GetRaw();
    public void SetRaw(object raw);
}

[Serializable]
public abstract class ComponentTemplateBase : IComponentTemplate
{
    public abstract Type Type { get; }
    public virtual bool IsUnique => true;

    public abstract object GetRaw();
    public abstract void SetRaw(object raw);
    public abstract void ApplyTo(int entityID, EcsWorld world);
    public abstract Task OnLoad(AssetsManager manager, int entityID, EcsWorld world);
}

[Serializable]
public abstract class ComponentTemplateBase<T> : ComponentTemplateBase, ICloneable where T : struct, IEcsMember
{
    private static bool _defaultValueTypeInit = false;
    private static T _defaultValueType;
    protected static T DefaultValueType
    {
        get
        {
            if (_defaultValueTypeInit) return _defaultValueType;
            var type = typeof(T);
            if (!type.IsValueType) return _defaultValueType;

            var fieldInfo = type.GetField("Default", BindingFlags.Static | BindingFlags.Public);
            if (fieldInfo != null && fieldInfo.FieldType == type)
            {
                _defaultValueType = (T)fieldInfo.GetValue(null);
            }
            fieldInfo = type.GetField("Empty", BindingFlags.Static | BindingFlags.Public);
            if (fieldInfo != null && fieldInfo.FieldType == type)
            {
                _defaultValueType = (T)fieldInfo.GetValue(null);
            }
            return _defaultValueType;
        }
    }

    public ComponentTemplateBase(T component)
    {
        _component = component;
    }

    protected T _component = DefaultValueType;

    public override Type Type => typeof(T);
    
    public override object GetRaw() => _component;
    public override void SetRaw(object raw) => _component = (T)raw;
    public T Value => _component;
    
    protected virtual T CloneComponent() => _component;
    public object Clone()
    {
        var templateClone = (ComponentTemplateBase<T>)MemberwiseClone();
        templateClone._component = CloneComponent();
        return templateClone;
    }
}

public class ComponentTemplate<T> : ComponentTemplateBase<T>
    where T : struct, IEcsComponent
{
    private static IEcsComponentOnLoad<T> _loader = null;
    
    public ComponentTemplate(T component) : base(component)
    {
        if (_loader == null && component is IEcsComponentOnLoad<T> c)
        {
            _loader = c;
        }
    }

    public override void ApplyTo(int entityID, EcsWorld world)
    {
        EcsPool<T>.Apply(ref _component, entityID, world.ID);
    }

    public override async Task OnLoad(AssetsManager manager, int entityID, EcsWorld world)
    {
        var pool = world.GetPool<T>();
        ref var c = ref pool.Get(entityID);
        if (_loader is not null)
        {
            var result = await _loader.OnLoad(c, manager);
            pool.Get(entityID) = result;
        }
    }
}
public class TagComponentTemplate<T> : ComponentTemplateBase<T>
    where T : struct, IEcsTagComponent
{
    public TagComponentTemplate(T component) : base(component)
    {
    }

    public override void ApplyTo(int entityID, EcsWorld world)
    {
        EcsTagPool<T>.Apply(ref _component, entityID, world.ID);
    }

    public override Task OnLoad(AssetsManager manager, int entityID, EcsWorld world)
    {
        return Task.CompletedTask;
    }
}
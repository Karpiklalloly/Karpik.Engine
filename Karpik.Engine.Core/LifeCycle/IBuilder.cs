namespace Karpik.Engine.Core;

public interface IBuilder
{
    public IBuilder Add(object system, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemInit init, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemBegin begin, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemFixedUpdate fixedUpdate, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemUpdate update, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemLate end, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemRender render, string layer = "BASIC_LAYER", int order = 0);
    public IBuilder Add(ISystemDestroy destroy, string layer = "BASIC_LAYER", int order = 0);

    public IBuilder AddRunner<TRunner>() where TRunner : new();
}

public interface IModule
{
    public void Import(IBuilder builder);
}
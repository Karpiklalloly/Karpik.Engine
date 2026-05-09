using DCFApixels.DragonECS;
using DragonExtensions;

namespace Karpik.Engine.Core.Runner;

public class Builder(EcsPipeline.Builder builder) : IBuilder
{
    public void Add(ISystemInit init, int order = 0)
    {
        builder.Add(new InitSystem(init), order);
    }

    public void Add(ISystemBegin begin, int order = 0)
    {
        builder.Add(new BeginSystem(begin), order);
    }

    public void Add(ISystemFixedUpdate fixedUpdate, int order = 0)
    {
        builder.Add(new FixedUpdateSystem(fixedUpdate), order);
    }

    public void Add(ISystemUpdate update, int order = 0)
    {
        builder.Add(new UpdateSystem(update), order);
    }

    public void Add(ISystemLate end, int order = 0)
    {
        builder.Add(new LateSystem(end), order);
    }

    public void Add(ISystemRender render, int order = 0)
    {
        builder.Add(new RenderSystem(render), order);
    }

    public void Add(ISystemDestroy destroy, int order = 0)
    {
        builder.Add(new DragonExtensions.DestroySystem(destroy), order);
    }
}
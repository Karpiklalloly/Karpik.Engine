namespace Karpik.Engine.Core;

public interface IBuilder
{
    public void Add(ISystemInit init, int order = 0);
    public void Add(ISystemBegin begin, int order = 0);
    public void Add(ISystemFixedUpdate fixedUpdate, int order = 0);
    public void Add(ISystemUpdate update, int order = 0);
    public void Add(ISystemLate end, int order = 0);
    public void Add(ISystemRender render, int order = 0);
    public void Add(ISystemDestroy destroy, int order = 0);
}
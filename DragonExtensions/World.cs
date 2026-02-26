using Karpik.Engine.Shared.DragonECS;

namespace DragonExtensions;

public class World(EcsWorld world)
{
    public entlong New() => world.NewEntityLong();

    public entlong New(int id) => world.NewEntityLong(id);

    public entlong New(ITemplateNode templateNode) => world.NewEntityLong(templateNode);

    public entlong New(int id, ITemplateNode templateNode) => world.NewEntityLong(id, templateNode);

    public ref T Event<T>() where T : struct, IEcsComponentEvent => ref world.GetPool<T>().Add(New().ID);

    public void Del(int id) => world.DelEntity(id);

    public void Del(entlong entity) => world.DelEntity(entity);

    public EcsSpan W<T>(out T aspect) where T : EcsAspect, new() => world.Where(out aspect);
}
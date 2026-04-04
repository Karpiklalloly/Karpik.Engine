namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct ResourceSetDescription
{
    public readonly IResourceLayout Layout;
    public readonly IGraphicsResource[] BoundResources; // Буферы и текстуры

    public ResourceSetDescription(IResourceLayout layout, params IGraphicsResource[] boundResources)
    {
        Layout = layout;
        BoundResources = boundResources;
    }
}
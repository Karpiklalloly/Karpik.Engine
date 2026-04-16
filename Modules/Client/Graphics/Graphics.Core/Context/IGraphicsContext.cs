namespace Karpik.Engine.Client.Graphics.Core;

public interface IGraphicsContext
{
    public ITextureFactory Textures { get; }
    public IShaderFactory Shaders { get; }
    public IPipelineFactory Pipelines { get; }
    public void Init();
    public void BeginFrame();
    public void Submit();
}
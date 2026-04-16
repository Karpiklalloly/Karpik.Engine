using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class VeldridShaderFactory(GraphicsDevice device) : IShaderFactory
{
    private readonly ResourceFactory _factory = device.ResourceFactory;
    
    public IShader Create(ShaderDescription description)
    {
        Shader? shader = _factory.CreateShader(description);
        if (shader is null)
        {
            throw new NullReferenceException($"Shader is null!");
        }
        return new VeldridShader(shader);
    }

    public void Destroy(IShader shader)
    {
        shader.Dispose();
    }
}
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IShaderFactory
{
    public IShader Create(ShaderDescription description);

    public void Destroy(IShader shader);
}
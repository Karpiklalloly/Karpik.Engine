using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Headless;

[Module(-102)]
public sealed class GraphicsHeadlessInstaller : IInstaller
{
    private readonly HeadlessGraphicsBackend _backend = new();

    public string Name => "Graphics.Headless";

    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register<IGraphicsBackend>(_backend);
    }
}

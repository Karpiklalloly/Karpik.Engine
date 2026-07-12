using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;

namespace Karpik.Engine.Modules.Window.Headless;

[Module(-200)]
public sealed class WindowHeadlessInstaller : IInstaller
{
    private readonly HeadlessWindow _window;
    private readonly HeadlessInputSource _inputSource;

    public WindowHeadlessInstaller()
    {
        _window = new HeadlessWindow();
        Controller = new HeadlessInputController();
        _inputSource = new HeadlessInputSource(Controller);
    }

    public string Name => "Window.Headless";

    public HeadlessInputController Controller { get; }

    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register(Controller);
        services.Register<IInputSource>(_inputSource);
        services.Register<IWindow>(_window);
    }
}

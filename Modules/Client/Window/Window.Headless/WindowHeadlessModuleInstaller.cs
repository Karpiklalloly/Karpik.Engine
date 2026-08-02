using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Modules.Window.Headless;

[Module(-200)]
public sealed class WindowHeadlessModuleInstaller : IModuleInstaller
{
    private readonly HeadlessWindow _window;
    private readonly HeadlessInputSource _inputSource;

    public WindowHeadlessModuleInstaller()
    {
        _window = new HeadlessWindow();
        Controller = new HeadlessInputController();
        _inputSource = new HeadlessInputSource(Controller);
    }

    public string Name => "Window.Headless";

    public HeadlessInputController Controller { get; }

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(Controller);
        builder.Register<IInputSource>(_inputSource);
        builder.Register<IWindow>(_window);
    }
}

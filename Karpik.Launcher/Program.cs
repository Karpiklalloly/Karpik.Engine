using System.Reflection;
using Avalonia;
using Karpik.Launcher.ViewModels;
using ReactiveUI.Avalonia;

namespace Karpik.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(x =>
            {
                x.WithViewsFromAssembly(Assembly.GetExecutingAssembly());
            });
}

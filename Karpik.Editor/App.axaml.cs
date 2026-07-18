using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Karpik.Editor;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            EditorStartupOptions options = EditorStartupOptions.Parse(desktop.Args);
            desktop.MainWindow = new MainWindow(options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

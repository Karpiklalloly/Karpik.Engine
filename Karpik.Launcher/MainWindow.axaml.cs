using Avalonia.Controls;
using Karpik.Launcher.Services;
using Karpik.Launcher.ViewModels;
using System.Reflection;

namespace Karpik.Launcher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new LauncherViewModel(
            new ProjectRegistry(),
            new EditorProcessHost(new EditorResolver(debugEditorDirectory: ReadDebugEditorDirectory())),
            StorageProvider,
            Clipboard);
        Host.ViewModel = viewModel;
        Closing += viewModel.OnClose;
    }

    private static string? ReadDebugEditorDirectory() =>
        typeof(MainWindow).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Karpik.DebugEditorDirectory")
            ?.Value;
}

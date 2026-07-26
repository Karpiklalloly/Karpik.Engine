using Avalonia.Controls;
using Karpik.Launcher.Services;
using Karpik.Launcher.ViewModels;

namespace Karpik.Launcher;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new LauncherViewModel(
            new ProjectRegistry(),
            new EditorProcessHost(),
            StorageProvider,
            Clipboard);
        Host.ViewModel = viewModel;
        Closing += viewModel.OnClose;
    }
}

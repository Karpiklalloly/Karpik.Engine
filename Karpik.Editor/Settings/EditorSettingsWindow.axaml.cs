using Avalonia.Controls;

namespace Karpik.Editor;

public sealed partial class EditorSettingsWindow : Window
{
    public EditorSettingsWindow()
    {
        InitializeComponent();
    }

    public EditorSettingsWindow(
        EditorUiDensity uiDensity,
        EditorLayoutPreset layoutPreset,
        Action<EditorUiDensity, EditorLayoutPreset> apply)
        : this()
    {
        DataContext = new EditorSettingsViewModel(uiDensity, layoutPreset, apply, Close);
    }
}

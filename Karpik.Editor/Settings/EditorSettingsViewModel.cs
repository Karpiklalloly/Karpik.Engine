using ReactiveUI;
using ReactiveUI.Primitives;

namespace Karpik.Editor;

public sealed class EditorSettingsViewModel : ReactiveObject
{
    private EditorUiDensity _uiDensity;
    private EditorLayoutPreset _layoutPreset;

    public EditorSettingsViewModel(
        EditorUiDensity uiDensity,
        EditorLayoutPreset layoutPreset,
        Action<EditorUiDensity, EditorLayoutPreset> apply,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(close);
        _uiDensity = uiDensity;
        _layoutPreset = layoutPreset;
        ApplyCommand = ReactiveCommand.Create(() =>
        {
            apply(UiDensity, LayoutPreset);
            close();
        });
        CancelCommand = ReactiveCommand.Create(close);
    }

    public EditorUiDensity[] UiDensities { get; } = Enum.GetValues<EditorUiDensity>();
    public EditorLayoutPreset[] LayoutPresets { get; } = Enum.GetValues<EditorLayoutPreset>();

    public EditorUiDensity UiDensity
    {
        get => _uiDensity;
        set => this.RaiseAndSetIfChanged(ref _uiDensity, value);
    }

    public EditorLayoutPreset LayoutPreset
    {
        get => _layoutPreset;
        set => this.RaiseAndSetIfChanged(ref _layoutPreset, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ApplyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }
}

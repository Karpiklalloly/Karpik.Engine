using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Karpik.Engine.Tooling;
using Karpik.Launcher.Models;
using Karpik.Launcher.Services;

namespace Karpik.Launcher;

public sealed record CreateProjectDialogResult(string SolutionPath, bool OpenAfterCreation);

public partial class CreateProjectWindow : Window
{
    private readonly IReadOnlyList<InstalledEngineInstallation> _installations;
    private readonly ProjectTemplateCatalogReader _catalog = new();
    public CreateProjectWindow(IReadOnlyList<InstalledEngineInstallation> installations)
    {
        _installations = installations;
        InitializeComponent();
        SdkBox.ItemsSource = installations;
        SdkBox.SelectedIndex = 0;
        RefreshTemplates();
    }
    private void SdkChanged(object? sender, SelectionChangedEventArgs e) => RefreshTemplates();
    private void RefreshTemplates()
    {
        if (SdkBox.SelectedItem is not InstalledEngineInstallation sdk) return;
        TemplateCatalogResult result = _catalog.Read(sdk.InstallationRoot);
        TemplateBox.ItemsSource = result.Templates;
        TemplateBox.SelectedIndex = result.Templates.Count > 0 ? 0 : -1;
        Status.Text = result.IsSuccess ? string.Empty : result.Message;
    }
    private async void Browse(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        DirectoryBox.Text = folders.SingleOrDefault()?.TryGetLocalPath() ?? DirectoryBox.Text;
    }
    private async void Create(object? sender, RoutedEventArgs e)
    {
        if (SdkBox.SelectedItem is not InstalledEngineInstallation sdk || TemplateBox.SelectedItem is not ProjectTemplate template) { Status.Text = "Выберите SDK и шаблон."; return; }
        ProjectCreationResult result = await new ProjectCreationService().CreateAsync(sdk.InstallationRoot, sdk.Manifest.MsBuildSdkVersion, template, DirectoryBox.Text ?? string.Empty, NameBox.Text ?? string.Empty, CancellationToken.None);
        Status.Text = result.Message;
        if (result.IsSuccess) Close(new CreateProjectDialogResult(result.SolutionPath!, OpenBox.IsChecked == true));
    }
    private void Cancel(object? sender, RoutedEventArgs e) => Close();
}

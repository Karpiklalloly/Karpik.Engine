using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Karpik.Launcher.Models;
using Karpik.Launcher.Services;

namespace Karpik.Launcher.ViewModels;

public sealed class LauncherViewModel : INotifyPropertyChanged
{
    private readonly ProjectRegistry _projectRegistry;
    private readonly IEditorProcessHost _editorHost;
    private bool _isBusy;
    private string _status = "Select a Karpik project.";

    public LauncherViewModel(
        ProjectRegistry? projectRegistry = null,
        IEditorProcessHost? editorHost = null)
    {
        _projectRegistry = projectRegistry ?? new ProjectRegistry();
        _editorHost = editorHost ?? new EditorProcessHost();
        ReloadRecentProjects();
    }

    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task<EditorHostResult> LaunchAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("An editor is already running.");
        }

        IsBusy = true;
        try
        {
            _projectRegistry.Add(solutionPath);
            ReloadRecentProjects();
            Status = $"Opening {Path.GetFileName(solutionPath)}...";
            EditorHostResult result = await _editorHost.RunAsync(solutionPath, cancellationToken);
            Status = result.Message;
            return result;
        }
        catch (OperationCanceledException)
        {
            Status = "Editor launch was cancelled.";
            throw;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReloadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (RecentProject project in _projectRegistry.Load())
        {
            RecentProjects.Add(project);
        }
        if (_projectRegistry.LastLoadDiagnostic is { } diagnostic)
        {
            Status = diagnostic;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

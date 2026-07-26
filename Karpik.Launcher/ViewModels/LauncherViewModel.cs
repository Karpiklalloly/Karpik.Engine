using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Karpik.Launcher.Models;
using Karpik.Launcher.Services;
using ReactiveUI;

namespace Karpik.Launcher.ViewModels;

public sealed class LauncherViewModel : ReactiveObject, ILauncherViewModel
{
    private readonly IStorageProvider _storageProvider;
    private readonly IClipboard? _clipboard;
    private readonly ProjectRegistry _projectRegistry;
    private readonly IEditorProcessHost _editorHost;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private string _status = "Select a Karpik project.";

    public LauncherViewModel(
        ProjectRegistry projectRegistry,
        IEditorProcessHost editorHost,
        IStorageProvider storageProvider,
        IClipboard? clipboard)
    {
        _storageProvider = storageProvider;
        _clipboard = clipboard;
        _projectRegistry = projectRegistry;
        _editorHost = editorHost;
        OpenProjectCommand = ReactiveCommand.Create(OpenProjectAsync);
        OpenRecentProjectCommand = ReactiveCommand.CreateFromTask<string>(OpenRecentProjectAsync);
        CopyErrorCommand = ReactiveCommand.CreateFromTask(CopyErrorAsync);
        ReloadRecentProjects();
    }

    public ObservableCollection<RecentProject> RecentProjects { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public ICommand OpenProjectCommand { get; }
    public ICommand OpenRecentProjectCommand { get; }
    public ICommand CopyErrorCommand { get; }

    public async Task<EditorHostResult> LaunchAsync(
        string solutionPath,
        CancellationToken cancellationToken)
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

    public void OnClose(object? o, WindowClosingEventArgs windowClosingEventArgs)
    {
        _lifetime.Cancel();
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
    
    private async Task OpenProjectAsync()
    {
        IReadOnlyList<IStorageFile> files = await _storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open Karpik project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Karpik solution") { Patterns = ["*.slnx"] }
                ]
            });
        string? solutionPath = files.SingleOrDefault()?.TryGetLocalPath();
        if (solutionPath is not null)
        {
            await LaunchAsync(solutionPath);
        }
    }
    
    private async Task OpenRecentProjectAsync(string solutionPath)
    {
        await LaunchAsync(solutionPath);
    }
    
    private async Task LaunchAsync(string solutionPath)
    {
        try
        {
            await LaunchAsync(solutionPath, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // LauncherViewModel publishes the actionable diagnostic through Status.
        }
    }
    
    private Task CopyErrorAsync()
    {
        return _clipboard?.SetTextAsync(Status) ?? Task.CompletedTask;
    }
}

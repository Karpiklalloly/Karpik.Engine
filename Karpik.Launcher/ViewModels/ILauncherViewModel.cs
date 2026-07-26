using System.Collections.ObjectModel;
using System.Windows.Input;
using Karpik.Launcher.Models;

namespace Karpik.Launcher.ViewModels;

public interface ILauncherViewModel
{
    public ObservableCollection<RecentProject> RecentProjects { get; }
    
    public bool IsBusy { get; }
    
    public string Status { get; }
    
    public ICommand OpenProjectCommand { get; }
    
    public ICommand OpenRecentProjectCommand { get; }
    
    public ICommand CopyErrorCommand { get; }
}
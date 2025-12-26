using TerminalHost.ViewModels;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Services;

public interface IViewModelFactory
{
    FileExplorerViewModel CreateFileExplorer(string rootPath);
    FileViewerViewModel CreateFileViewer(bool isDetached = false);
    DashboardTabViewModel CreateDashboard(MainViewModel parent);
    WorkspaceSidebarViewModel CreateWorkspaceSidebar();
    SettingsTabViewModel CreateSettings();
    StatisticsTabViewModel CreateStatistics();
}

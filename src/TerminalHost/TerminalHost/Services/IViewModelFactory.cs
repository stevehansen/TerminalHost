using TerminalHost.ViewModels;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Services;

public interface IViewModelFactory
{
    FileExplorerViewModel CreateFileExplorer(string rootPath);
    FileViewerViewModel CreateFileViewer();
    DashboardTabViewModel CreateDashboard(MainViewModel parent);
    WorkspaceSidebarViewModel CreateWorkspaceSidebar();
    SettingsTabViewModel CreateSettings();
    StatisticsTabViewModel CreateStatistics();
    TimelineTabViewModel CreateTimeline();
    SparkCanvasViewModel CreateSparkCanvas();
}

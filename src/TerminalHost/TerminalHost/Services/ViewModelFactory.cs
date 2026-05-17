using System;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

public class ViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public FileExplorerViewModel CreateFileExplorer(string rootPath)
    {
        // FileExplorerViewModel has 7 dependencies
        return new FileExplorerViewModel(
            _serviceProvider.GetRequiredService<IFileExplorerService>(),
            _serviceProvider.GetRequiredService<IGitStatusService>(),
            _serviceProvider.GetRequiredService<IGitIgnoreService>(),
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<IFileSystem>(),
            _serviceProvider.GetRequiredService<IProcessService>(),
            _serviceProvider.GetRequiredService<IToastService>()
        )
        {
            // Initialize with the path
             RootPath = rootPath
        };
    }

    public FileViewerViewModel CreateFileViewer(bool isDetached = false)
    {
        var vm = new FileViewerViewModel(
            _serviceProvider.GetRequiredService<IFilePreviewService>(),
            _serviceProvider.GetRequiredService<IFileEditService>(),
            _serviceProvider.GetRequiredService<IFileSystem>(),
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<IMarkdownService>(),
            _serviceProvider.GetRequiredService<ITimerService>()
        );
        vm.IsDetached = isDetached;
        return vm;
    }

    public DashboardTabViewModel CreateDashboard(MainViewModel parent)
    {
        return new DashboardTabViewModel(
            _serviceProvider.GetRequiredService<IGitHubService>(),
            _serviceProvider.GetRequiredService<IConfigurationService>(),
            parent,
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<IFileSystem>(),
            _serviceProvider.GetRequiredService<IProcessService>(),
            _serviceProvider.GetRequiredService<IToastService>(),
            _serviceProvider.GetRequiredService<ITimerService>(),
            _serviceProvider.GetRequiredService<IFolderPickerService>(),
            _serviceProvider.GetRequiredService<IAiExecutionService>()
        );
    }

    public WorkspaceSidebarViewModel CreateWorkspaceSidebar()
    {
        return new WorkspaceSidebarViewModel(
            _serviceProvider.GetRequiredService<IConfigurationService>(),
            _serviceProvider.GetRequiredService<IGitWorktreeService>(),
            _serviceProvider.GetRequiredService<IGitStatusService>(),
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<IFileSystem>(),
            _serviceProvider.GetRequiredService<IStatisticsService>(),
            _serviceProvider.GetRequiredService<IToastService>()
        );
    }

    public SettingsTabViewModel CreateSettings()
    {
        return new SettingsTabViewModel(
            _serviceProvider.GetRequiredService<IConfigurationService>(),
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<IToastService>(),
            _serviceProvider.GetRequiredService<IProcessService>(),
            _serviceProvider.GetRequiredService<IClipboardService>(),
            _serviceProvider.GetRequiredService<IContainerService>(),
            _serviceProvider.GetRequiredService<IFileSystem>(),
            _serviceProvider.GetService<IEidetService>(),
            _serviceProvider.GetService<IContainerConfiguration>()
        );
    }
    
    public StatisticsTabViewModel CreateStatistics()
    {
        return new StatisticsTabViewModel(
            _serviceProvider.GetRequiredService<IStatisticsService>()
        );
    }

    public TimelineTabViewModel CreateTimeline()
    {
        return new TimelineTabViewModel(
            _serviceProvider.GetRequiredService<ITimelineService>(),
            _serviceProvider.GetService<IClaudeSessionIndexService>(),
            _serviceProvider.GetRequiredService<IDialogService>(),
            _serviceProvider.GetRequiredService<ITimerService>(),
            _serviceProvider.GetService<ISessionActivityService>(),
            _serviceProvider.GetRequiredService<IProcessService>(),
            _serviceProvider.GetRequiredService<IClipboardService>()
        );
    }

    public SparkCanvasViewModel CreateSparkCanvas()
    {
        return new SparkCanvasViewModel(
            _serviceProvider.GetService<ISessionActivityService>(),
            _serviceProvider.GetService<IApiServer>(),
            _serviceProvider.GetService<ITimelineService>(),
            _serviceProvider.GetRequiredService<IConfigurationService>()
        );
    }
}

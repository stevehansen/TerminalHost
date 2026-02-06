using System.Windows;
using System.Windows.Controls;
using TerminalHost.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitFilesView : UserControl
{
    private HunkStagingDiffViewer? _hunkDiffViewer;

    public GitFilesView()
    {
        InitializeComponent();
    }

    private void HunkDiffViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is HunkStagingDiffViewer viewer && _hunkDiffViewer == null)
        {
            _hunkDiffViewer = viewer;

            var diffParser = App.Current.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
            if (diffParser != null)
            {
                viewer.SetDiffParser(diffParser);
            }

            if (DataContext is GitFilesViewModel vm)
            {
                viewer.HunkStageRequested += (s, hunkIndex) =>
                {
                    if (vm.StageHunkCommand.CanExecute(hunkIndex))
                        vm.StageHunkCommand.Execute(hunkIndex);
                };

                viewer.HunkUnstageRequested += (s, hunkIndex) =>
                {
                    if (vm.UnstageHunkCommand.CanExecute(hunkIndex))
                        vm.UnstageHunkCommand.Execute(hunkIndex);
                };
            }
        }
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNode node && !node.IsFolder && node.FileStatus != null)
        {
            if (DataContext is GitFilesViewModel vm)
            {
                vm.SelectedGitFile = node.FileStatus;
            }
        }
    }
}
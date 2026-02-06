using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Content view for Git Changes panel.
/// This view displays the content without any popup/window chrome,
/// making it suitable for use in the panel system (docked, popup, or window).
/// </summary>
public partial class GitFilesContentView : UserControl
{
    public GitFilesContentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is GitFilesViewModel vm)
        {
            // Wire up hunk staging events
            var diffParser = App.Current.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
            if (diffParser != null)
            {
                HunkDiffViewer.SetDiffParser(diffParser);
            }

            HunkDiffViewer.HunkStageRequested += (s, hunkIndex) =>
            {
                if (vm.StageHunkCommand.CanExecute(hunkIndex))
                    vm.StageHunkCommand.Execute(hunkIndex);
            };

            HunkDiffViewer.HunkUnstageRequested += (s, hunkIndex) =>
            {
                if (vm.UnstageHunkCommand.CanExecute(hunkIndex))
                    vm.UnstageHunkCommand.Execute(hunkIndex);
            };
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

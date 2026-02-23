using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private bool _isSyncingSelection;
    private GitFilesViewModel? _currentVm;

    public GitFilesContentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old events to prevent accumulation
        if (_currentVm != null)
        {
            HunkDiffViewer.HunkStageRequested -= OnHunkStageRequested;
            HunkDiffViewer.HunkUnstageRequested -= OnHunkUnstageRequested;
            HunkDiffViewer.HunkDiscardRequested -= OnHunkDiscardRequested;
            HunkDiffViewer.FixInvisibleChangesRequested -= OnFixInvisibleChangesRequested;
        }

        if (e.NewValue is GitFilesViewModel vm)
        {
            _currentVm = vm;

            // Wire up hunk staging events
            var diffParser = App.Current.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
            if (diffParser != null)
            {
                HunkDiffViewer.SetDiffParser(diffParser);
            }

            HunkDiffViewer.HunkStageRequested += OnHunkStageRequested;
            HunkDiffViewer.HunkUnstageRequested += OnHunkUnstageRequested;
            HunkDiffViewer.HunkDiscardRequested += OnHunkDiscardRequested;
            HunkDiffViewer.FixInvisibleChangesRequested += OnFixInvisibleChangesRequested;
        }
        else
        {
            _currentVm = null;
        }
    }

    private void OnHunkStageRequested(object? sender, int hunkIndex)
    {
        if (_currentVm?.StageHunkCommand.CanExecute(hunkIndex) == true)
            _currentVm.StageHunkCommand.Execute(hunkIndex);
    }

    private void OnHunkUnstageRequested(object? sender, int hunkIndex)
    {
        if (_currentVm?.UnstageHunkCommand.CanExecute(hunkIndex) == true)
            _currentVm.UnstageHunkCommand.Execute(hunkIndex);
    }

    private void OnHunkDiscardRequested(object? sender, int hunkIndex)
    {
        if (_currentVm?.DiscardHunkCommand.CanExecute(hunkIndex) == true)
            _currentVm.DiscardHunkCommand.Execute(hunkIndex);
    }

    private void OnFixInvisibleChangesRequested(object? sender, EventArgs e)
    {
        if (_currentVm?.FixInvisibleChangesCommand.CanExecute(null) == true)
            _currentVm.FixInvisibleChangesCommand.Execute(null);
    }

    private void StagedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        if (e.AddedItems.Count == 0) return;

        _isSyncingSelection = true;
        try
        {
            UnstagedList.SelectedItem = null;
            if (DataContext is GitFilesViewModel vm)
                vm.SelectedGitFile = e.AddedItems[0] as GitFileStatus;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void UnstagedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        if (e.AddedItems.Count == 0) return;

        _isSyncingSelection = true;
        try
        {
            StagedList.SelectedItem = null;
            if (DataContext is GitFilesViewModel vm)
                vm.SelectedGitFile = e.AddedItems[0] as GitFileStatus;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void CommitMessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (DataContext is GitFilesViewModel vm && vm.CreateCommitCommand.CanExecute(null))
            {
                vm.CreateCommitCommand.Execute(null);
                e.Handled = true;
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

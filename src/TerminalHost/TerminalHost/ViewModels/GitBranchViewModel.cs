using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media; // Added for SolidColorBrush
using System.Windows.Controls; // Added for StackPanel, TextBlock, TextBox, Button
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitBranchViewModel : ObservableObject
{
    private readonly GitStatusService _gitStatusService;
    private readonly MainViewModel _mainViewModel; // To get selected tab and refresh its git status

    private List<GitBranch> _allBranches = new();

    [ObservableProperty]
    private string _currentBranchWorkingDirectory = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitBranch> _branches = new();

    [ObservableProperty]
    private GitBranch? _selectedBranch;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _title = "Git Branches";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 1100;
    [ObservableProperty]
    private double _height = 700;
    [ObservableProperty]
    private double _horizontalOffset;
    [ObservableProperty]
    private double _verticalOffset;

    public GitBranchViewModel(GitStatusService gitStatusService, MainViewModel mainViewModel)
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;

        // GitBranch is opened via MainWindow.xaml.cs directly (Ctrl+B shortcut)
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            StatusMessage = "Please select a project tab first.";
            IsOpen = true; // Still open to show message
            return;
        }

        CurrentBranchWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git Branches - {terminalTab.Title}";
        StatusMessage = string.Empty; // Clear previous messages

        await RefreshGitBranchesAsync();

        IsOpen = true;

        // Reset search text and focus should be handled by the view
        SearchText = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshGitBranchesAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
        {
            StatusMessage = "No project directory selected.";
            return;
        }

        IsLoading = true;
        try
        {
            _allBranches = await _gitStatusService.GetBranchesAsync(CurrentBranchWorkingDirectory);
            ApplyBranchFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyBranchFilter();
    }

    private void ApplyBranchFilter()
    {
        var searchTextLower = SearchText.ToLower();

        IEnumerable<GitBranch> filtered = _allBranches;
        if (!string.IsNullOrEmpty(searchTextLower))
        {
            filtered = _allBranches.Where(b =>
                b.Name.ToLower().Contains(searchTextLower) ||
                b.ShortName.ToLower().Contains(searchTextLower) ||
                (b.IssueNumber?.ToLower().Contains(searchTextLower) ?? false));
        }

        // Group branches by type
        var view = new CollectionViewSource { Source = filtered.ToList() }.View;
        view.GroupDescriptions.Add(new PropertyGroupDescription("TypeGroup"));

        Branches = new ObservableCollection<GitBranch>(view.Cast<GitBranch>());

        // Show/hide empty state
        StatusMessage = Branches.Any() ? string.Empty : "No branches found matching your search.";

        // Select first item if any (or current branch if present)
        SelectedBranch = Branches.FirstOrDefault(b => b.IsCurrent) ?? Branches.FirstOrDefault();

        UpdateBranchButtonsCanExecute();
    }

    partial void OnSelectedBranchChanged(GitBranch? value)
    {
        UpdateBranchButtonsCanExecute();
    }

    private void UpdateBranchButtonsCanExecute()
    {
        CheckoutBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCheckoutBranch))]
    private async Task CheckoutBranchAsync()
    {
        if (SelectedBranch == null || SelectedBranch.IsCurrent || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CheckoutBranchAsync(CurrentBranchWorkingDirectory, SelectedBranch.Name);

            if (result.Success)
            {
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to switch branch:\n{result.Error}",
                    "Git Checkout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanCheckoutBranch() => SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        // For now, use MessageBox.Show to simulate an input dialog.
        // In a real WPF app, this would be a custom dialog service.
        var branchName = ShowInputDialog("Create New Branch", "Enter branch name:", "");
        if (string.IsNullOrWhiteSpace(branchName))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateBranchAsync(CurrentBranchWorkingDirectory, branchName);

            if (result.Success)
            {
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to create branch:\n{result.Error}",
                    "Git Branch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranchAsync()
    {
        if (SelectedBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        if (SelectedBranch.IsCurrent)
        {
            MessageBox.Show(
                "Cannot delete the current branch. Please switch to another branch first.",
                "Git Branch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            if (SelectedBranch.IsRemote)
            {
                var remoteResult = MessageBox.Show(
                    $"Are you sure you want to delete the remote branch '{SelectedBranch.Name}'?\n\nThis action CANNOT be undone and will affect other developers.",
                    "Delete Remote Branch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (remoteResult != MessageBoxResult.Yes)
                    return;

                var remoteName = SelectedBranch.RemoteName ?? "origin";
                var result = await _gitStatusService.DeleteRemoteBranchAsync(
                    CurrentBranchWorkingDirectory,
                    remoteName,
                    SelectedBranch.ShortName);

                if (result.Success)
                {
                    await RefreshGitBranchesAsync();
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to delete remote branch:\n{result.Error}",
                        "Git Branch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                var localResult = MessageBox.Show(
                    $"Delete local branch '{SelectedBranch.Name}'?",
                    "Delete Branch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (localResult != MessageBoxResult.Yes)
                    return;

                var result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, SelectedBranch.Name);

                if (!result.Success && result.Error?.Contains("not fully merged") == true)
                {
                    var forceResult = MessageBox.Show(
                        $"Branch '{SelectedBranch.Name}' is not fully merged.\n\nDo you want to force delete it? This may result in lost commits.",
                        "Force Delete?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (forceResult == MessageBoxResult.Yes)
                    {
                        result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, SelectedBranch.Name, force: true);
                    }
                }

                if (result.Success)
                {
                    await RefreshGitBranchesAsync();
                }
                else if (result.Error?.Contains("not fully merged") != true)
                {
                    MessageBox.Show(
                        $"Failed to delete branch:\n{result.Error}",
                        "Git Branch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteBranch() => SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    [RelayCommand]
    private async Task FetchAllAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.FetchAllAsync(CurrentBranchWorkingDirectory);

            if (result.Success)
            {
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to fetch:\n{result.Error}",
                    "Git Fetch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.PullAsync(CurrentBranchWorkingDirectory);

            if (result.Success)
            {
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
                IsOpen = false; // Close after successful pull
            }
            else
            {
                MessageBox.Show(
                    $"Failed to pull:\n{result.Error}",
                    "Git Pull",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    private async Task RefreshTerminalGitStatusAsync()
    {
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            try
            {
                var status = await _gitStatusService.GetGitStatusAsync(terminalTab.Pair.WorkingDirectory);
                terminalTab.GitStatus = status;
            }
            catch
            {
                // Silently ignore git status errors
            }
        }
    }

    private static string? ShowInputDialog(string title, string prompt, string defaultValue)
    {
        // This method should ideally be replaced by a proper dialog service
        // For now, mirroring the existing implementation from MainWindow
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25))
        };

        var panel = new StackPanel { Margin = new Thickness(16) };

        var label = new TextBlock
        {
            Text = prompt,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(label);

        var textBox = new TextBox
        {
            Text = defaultValue,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "Create",
            Width = 80,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        okButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttonPanel.Children.Add(okButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0),
            IsCancel = true
        };
        cancelButton.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        buttonPanel.Children.Add(cancelButton);

        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        textBox.Focus();
        textBox.SelectAll();

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }
}

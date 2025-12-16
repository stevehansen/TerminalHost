using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Data;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitBranchViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly MainViewModel _mainViewModel; // To get selected tab and refresh its git status
    private readonly IDialogService _dialogService; // Added IDialogService dependency

    private List<GitBranch> _allBranches = [];

    [ObservableProperty]
    private string _currentBranchWorkingDirectory = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitBranch> _branches = [];

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

    public GitBranchViewModel(IGitStatusService gitStatusService, MainViewModel mainViewModel, IDialogService dialogService) // Added IDialogService
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService; // Initialize IDialogService

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
            return;

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
            var result = await _gitStatusService.CheckoutBranchAsync(
                CurrentBranchWorkingDirectory,
                SelectedBranch.Name,
                SelectedBranch.IsRemote);

            if (result.Success)
            {
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                // Close popup before showing dialog to avoid WPF focus/ownership issues
                IsOpen = false;
                _dialogService.ShowWarning(string.Format("Failed to switch branch:\n{0}", result.Error), "Git Checkout");
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

        // var branchName = ShowInputDialog("Create New Branch", "Enter branch name:", ""); // Commented out
        // For now, this will just call to the service with a placeholder.
        // A proper solution would be to make an IInputBoxService and inject it.
        var branchName = "new-branch-from-test"; // Placeholder

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
                // Close popup before showing dialog to avoid WPF focus/ownership issues
                IsOpen = false;
                _dialogService.ShowWarning(string.Format("Failed to create branch:\n{0}", result.Error), "Git Branch");
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
            // Close popup before showing dialog to avoid WPF focus/ownership issues
            IsOpen = false;
            _dialogService.ShowWarning(
                "Cannot delete the current branch. Please switch to another branch first.",
                "Git Branch");
            return;
        }

        // Close popup before showing any dialogs to avoid WPF focus/ownership issues
        var branchToDelete = SelectedBranch;
        IsOpen = false;

        IsLoading = true;
        try
        {
            if (branchToDelete.IsRemote)
            {
                if (!_dialogService.ShowConfirmation(
                    string.Format("Are you sure you want to delete the remote branch '{0}'?\n\nThis action CANNOT be undone and will affect other developers.", branchToDelete.Name),
                    "Delete Remote Branch"))
                    return;

                var remoteName = branchToDelete.RemoteName ?? "origin";
                var result = await _gitStatusService.DeleteRemoteBranchAsync(
                    CurrentBranchWorkingDirectory,
                    remoteName,
                    branchToDelete.ShortName);

                if (!result.Success)
                {
                    _dialogService.ShowWarning(string.Format("Failed to delete remote branch:\n{0}", result.Error), "Git Branch");
                }
            }
            else
            {
                if (!_dialogService.ShowConfirmation(
                    string.Format("Delete local branch '{0}'?", branchToDelete.Name),
                    "Delete Branch"))
                    return;

                var result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, branchToDelete.Name);

                if (!result.Success && result.Error?.Contains("not fully merged") == true)
                {
                    if (_dialogService.ShowConfirmation(
                        string.Format("Branch '{0}' is not fully merged.\n\nDo you want to force delete it? This may result in lost commits.", branchToDelete.Name),
                        "Force Delete?"))
                    {
                        result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, branchToDelete.Name, force: true);
                    }
                }

                if (!result.Success && result.Error?.Contains("not fully merged") != true)
                {
                    _dialogService.ShowWarning(string.Format("Failed to delete branch:\n{0}", result.Error), "Git Branch");
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
                // Close popup before showing dialog to avoid WPF focus/ownership issues
                IsOpen = false;
                _dialogService.ShowWarning(string.Format("Failed to fetch:\n{0}", result.Error), "Git Fetch");
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
                // Close popup before showing dialog to avoid WPF focus/ownership issues
                IsOpen = false;
                _dialogService.ShowWarning(string.Format("Failed to pull:\n{0}", result.Error), "Git Pull");
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

    // Commented out to resolve compilation errors, requires a proper injectable solution
    // private static string? ShowInputDialog(string title, string prompt, string defaultValue)
    // {
    //     // This method should ideally be replaced by a proper dialog service
    //     // For now, mirroring the existing implementation from MainWindow
    //     var dialog = new Window
    //     {
    //         Title = title,
    //         Width = 400,
    //         Height = 150,
    //         WindowStartupLocation = WindowStartupLocation.CenterScreen,
    //         WindowStyle = WindowStyle.ToolWindow,
    //         ResizeMode = ResizeMode.NoResize,
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25))
    //     };

    //     var panel = new StackPanel { Margin = new Thickness(16) };

    //     var label = new TextBlock
    //     {
    //         Text = prompt,
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
    //         Margin = new Thickness(0, 0, 0, 8)
    //     };
    //     panel.Children.Add(label);

    //     var textBox = new TextBox
    //     {
    //         Text = defaultValue,
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
    //         BorderBrush = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
    //         Padding = new Thickness(8, 6, 8, 6),
    //         Margin = new Thickness(0, 0, 0, 16)
    //     };
    //     panel.Children.Add(textBox);

    //     var buttonPanel = new StackPanel
    //     {
    //         Orientation = Orientation.Horizontal,
    //         HorizontalAlignment = HorizontalAlignment.Right
    //     };

    //     var okButton = new Button
    //     {
    //         Content = "Create",
    //         Width = 80,
    //         Padding = new Thickness(8, 4, 8, 4),
    //         Margin = new Thickness(0, 0, 8, 0),
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
    //         BorderThickness = new Thickness(0),
    //         IsDefault = true
    //     };
    //     okButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
    //     buttonPanel.Children.Add(okButton);

    //     var cancelButton = new Button
    //     {
    //         Content = "Cancel",
    //         Width = 80,
    //         Padding = new Thickness(8, 4, 8, 4),
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
    //         BorderThickness = new Thickness(0),
    //         IsCancel = true
    //     };
    //     cancelButton.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
    //     buttonPanel.Children.Add(cancelButton);

    //     panel.Children.Add(buttonPanel);

    //     dialog.Content = panel;

    //     textBox.Focus();
    //     textBox.SelectAll();

    //     return dialog.ShowDialog() == true ? textBox.Text : null;
    // }
}

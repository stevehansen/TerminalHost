using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace TerminalHost;

/// <summary>
/// Git branch popup logic.
/// </summary>
public partial class MainWindow
{
    private List<GitBranch> _allBranches = new();
    private string? _currentBranchWorkingDirectory;

    private async void ShowGitBranch()
    {
        // Get current working directory from selected terminal tab
        if (_viewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            MessageBox.Show(
                "Please select a project tab first.",
                "Git Branches",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _currentBranchWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        GitBranchTitle.Text = $"Git Branches - {terminalTab.Title}";

        // Load branches
        await RefreshGitBranches();

        GitBranchPopup.IsOpen = true;
        GitBranchSearchBox.Text = "";
        GitBranchSearchBox.Focus();
    }

    private async Task RefreshGitBranches()
    {
        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        _allBranches = await _gitStatusService.GetBranchesAsync(_currentBranchWorkingDirectory);

        ApplyBranchFilter();
    }

    private void ApplyBranchFilter()
    {
        var searchText = GitBranchSearchBox.Text?.ToLower() ?? "";

        IEnumerable<GitBranch> filtered = _allBranches;
        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = _allBranches.Where(b =>
                b.Name.ToLower().Contains(searchText) ||
                b.ShortName.ToLower().Contains(searchText) ||
                (b.IssueNumber?.ToLower().Contains(searchText) ?? false));
        }

        // Group branches by type
        var view = CollectionViewSource.GetDefaultView(filtered.ToList());
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription("TypeGroup"));

        GitBranchList.ItemsSource = view;

        // Show/hide empty state
        var hasItems = filtered.Any();
        GitBranchEmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        // Select first item if any
        if (hasItems)
        {
            GitBranchList.SelectedIndex = 0;
        }

        UpdateBranchButtons();
    }

    private void UpdateBranchButtons()
    {
        var selectedBranch = GitBranchList.SelectedItem as GitBranch;
        var hasSelection = selectedBranch != null;
        var canSwitch = hasSelection && !selectedBranch!.IsCurrent;
        var canDelete = hasSelection && !selectedBranch!.IsCurrent;

        GitBranchCheckoutButton.IsEnabled = canSwitch;
        GitBranchDeleteButton.IsEnabled = canDelete;
    }

    private void GitBranchSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyBranchFilter();
    }

    private void GitBranchSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (GitBranchList.SelectedIndex < GitBranchList.Items.Count - 1)
            {
                GitBranchList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (GitBranchList.SelectedIndex > 0)
            {
                GitBranchList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CheckoutSelectedBranch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            GitBranchPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void GitBranchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBranchButtons();
    }

    private void GitBranchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CheckoutSelectedBranch();
    }

    private async void CheckoutSelectedBranch()
    {
        if (GitBranchList.SelectedItem is not GitBranch branch)
            return;

        if (branch.IsCurrent)
            return;

        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        var result = await _gitStatusService.CheckoutBranchAsync(_currentBranchWorkingDirectory, branch.Name);

        if (result.Success)
        {
            GitBranchPopup.IsOpen = false;

            // Refresh git status in the terminal tab
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                await RefreshTerminalGitStatusAsync(terminalTab);
            }
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

    private async Task RefreshTerminalGitStatusAsync(TerminalPairTabViewModel terminalTab)
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

    private void GitBranchCheckout_Click(object sender, RoutedEventArgs e)
    {
        CheckoutSelectedBranch();
    }

    private async void GitBranchCreate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        // Simple input dialog using InputBox-style approach
        var branchName = ShowInputDialog("Create New Branch", "Enter branch name:", "");
        if (string.IsNullOrWhiteSpace(branchName))
            return;

        var result = await _gitStatusService.CreateBranchAsync(_currentBranchWorkingDirectory, branchName);

        if (result.Success)
        {
            GitBranchPopup.IsOpen = false;

            // Refresh git status in the terminal tab
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                await RefreshTerminalGitStatusAsync(terminalTab);
            }
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

    private async void GitBranchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GitBranchList.SelectedItem is not GitBranch branch)
            return;

        if (branch.IsCurrent)
        {
            MessageBox.Show(
                "Cannot delete the current branch. Please switch to another branch first.",
                "Git Branch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        if (branch.IsRemote)
        {
            // Extra confirmation for remote branch deletion
            var remoteResult = MessageBox.Show(
                $"Are you sure you want to delete the remote branch '{branch.Name}'?\n\nThis action CANNOT be undone and will affect other developers.",
                "Delete Remote Branch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (remoteResult != MessageBoxResult.Yes)
                return;

            var remoteName = branch.RemoteName ?? "origin";
            var result = await _gitStatusService.DeleteRemoteBranchAsync(
                _currentBranchWorkingDirectory,
                remoteName,
                branch.ShortName);

            if (result.Success)
            {
                await RefreshGitBranches();
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
            // Local branch deletion
            var localResult = MessageBox.Show(
                $"Delete local branch '{branch.Name}'?",
                "Delete Branch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (localResult != MessageBoxResult.Yes)
                return;

            var result = await _gitStatusService.DeleteBranchAsync(_currentBranchWorkingDirectory, branch.Name);

            if (!result.Success && result.Error?.Contains("not fully merged") == true)
            {
                // Ask if they want to force delete
                var forceResult = MessageBox.Show(
                    $"Branch '{branch.Name}' is not fully merged.\n\nDo you want to force delete it? This may result in lost commits.",
                    "Force Delete?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (forceResult == MessageBoxResult.Yes)
                {
                    result = await _gitStatusService.DeleteBranchAsync(_currentBranchWorkingDirectory, branch.Name, force: true);
                }
            }

            if (result.Success)
            {
                await RefreshGitBranches();
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

    private async void GitBranchFetch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        var result = await _gitStatusService.FetchAllAsync(_currentBranchWorkingDirectory);

        if (result.Success)
        {
            await RefreshGitBranches();

            // Refresh git status in the terminal tab
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                await RefreshTerminalGitStatusAsync(terminalTab);
            }
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

    private async void GitBranchPull_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentBranchWorkingDirectory))
            return;

        var result = await _gitStatusService.PullAsync(_currentBranchWorkingDirectory);

        if (result.Success)
        {
            await RefreshGitBranches();

            // Refresh git status in the terminal tab
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                await RefreshTerminalGitStatusAsync(terminalTab);
            }

            GitBranchPopup.IsOpen = false;
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

    private void GitBranchClose_Click(object sender, RoutedEventArgs e)
    {
        GitBranchPopup.IsOpen = false;
    }

    private static string? ShowInputDialog(string title, string prompt, string defaultValue)
    {
        // Create a simple input dialog window
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
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
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

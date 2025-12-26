using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Views.Dialogs;

/// <summary>
/// Dialog for creating a new git worktree with branch selection and path configuration.
/// </summary>
public partial class CreateWorktreeDialog : Window
{
    private readonly string _repoPath;
    private readonly string _suggestedBasePath;
    private readonly List<GitBranch> _branches;

    #region Dependency Properties

    public static readonly DependencyProperty BranchNameProperty =
        DependencyProperty.Register(nameof(BranchName), typeof(string), typeof(CreateWorktreeDialog),
            new PropertyMetadata(string.Empty, OnBranchNameChanged));

    public static readonly DependencyProperty WorktreePathProperty =
        DependencyProperty.Register(nameof(WorktreePath), typeof(string), typeof(CreateWorktreeDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CreateNewBranchProperty =
        DependencyProperty.Register(nameof(CreateNewBranch), typeof(bool), typeof(CreateWorktreeDialog),
            new PropertyMetadata(true, OnBranchModeChanged));

    public static readonly DependencyProperty UseExistingBranchProperty =
        DependencyProperty.Register(nameof(UseExistingBranch), typeof(bool), typeof(CreateWorktreeDialog),
            new PropertyMetadata(false));

    public static readonly DependencyProperty OpenAfterCreationProperty =
        DependencyProperty.Register(nameof(OpenAfterCreation), typeof(bool), typeof(CreateWorktreeDialog),
            new PropertyMetadata(true));

    public string BranchName
    {
        get => (string)GetValue(BranchNameProperty);
        set => SetValue(BranchNameProperty, value);
    }

    public string WorktreePath
    {
        get => (string)GetValue(WorktreePathProperty);
        set => SetValue(WorktreePathProperty, value);
    }

    public bool CreateNewBranch
    {
        get => (bool)GetValue(CreateNewBranchProperty);
        set => SetValue(CreateNewBranchProperty, value);
    }

    public bool UseExistingBranch
    {
        get => (bool)GetValue(UseExistingBranchProperty);
        set => SetValue(UseExistingBranchProperty, value);
    }

    public bool OpenAfterCreation
    {
        get => (bool)GetValue(OpenAfterCreationProperty);
        set => SetValue(OpenAfterCreationProperty, value);
    }

    #endregion

    public bool Confirmed { get; private set; }

    /// <summary>
    /// Creates a new CreateWorktreeDialog.
    /// </summary>
    /// <param name="repoPath">Path to the repository (used for validation and suggestions).</param>
    /// <param name="branches">Available branches for selection.</param>
    /// <param name="suggestedBasePath">Base path for auto-generated worktree location.</param>
    public CreateWorktreeDialog(string repoPath, IEnumerable<GitBranch> branches, string suggestedBasePath)
    {
        InitializeComponent();

        _repoPath = repoPath;
        _suggestedBasePath = suggestedBasePath;
        // Sort: local branches first (by SortOrder), then by name
        _branches = branches.Where(b => !b.IsCurrent).OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToList();

        // Populate branch list (flat list, sorted local first then remote)
        BranchList.ItemsSource = _branches;

        // Set initial suggested path hint
        UpdateSuggestedPathHint();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the branch text box
        BranchTextBox.Focus();
    }

    private static void OnBranchNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateWorktreeDialog dialog)
        {
            dialog.UpdateSuggestedPath();
            dialog.UpdateSuggestedPathHint();
            dialog.ValidateInput();
        }
    }

    private static void OnBranchModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateWorktreeDialog dialog)
        {
            dialog.ValidateInput();
        }
    }

    private void UpdateSuggestedPath()
    {
        if (string.IsNullOrWhiteSpace(BranchName))
            return;

        // Auto-generate path: basePath-branchName (sanitized)
        var sanitizedBranch = SanitizeBranchForPath(BranchName);
        var repoName = Path.GetFileName(_repoPath);
        var parentDir = Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath;

        WorktreePath = Path.Combine(parentDir, $"{repoName}.{sanitizedBranch}");
    }

    private void UpdateSuggestedPathHint()
    {
        var repoName = Path.GetFileName(_repoPath);
        SuggestedPathHint.Text = $"Suggested: {{parent-folder}}\\{repoName}.{{branch}}";
    }

    private static string SanitizeBranchForPath(string branchName)
    {
        // Remove common prefixes
        var sanitized = branchName;
        if (sanitized.StartsWith("feature/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[8..];
        else if (sanitized.StartsWith("bugfix/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[7..];
        else if (sanitized.StartsWith("hotfix/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[7..];
        else if (sanitized.StartsWith("release/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[8..];

        // Replace invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '-');
        }

        // Replace slashes with dashes
        sanitized = sanitized.Replace('/', '-').Replace('\\', '-');

        return sanitized;
    }

    private void ValidateInput()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BranchName))
        {
            errors.Add("Branch name is required.");
        }
        else if (!CreateNewBranch)
        {
            // Check if branch exists - match by ShortName (covers both local and remote)
            // e.g., "#95" matches local "#95" or remote "origin/#95"
            var exists = _branches.Any(b =>
                b.ShortName.Equals(BranchName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                errors.Add($"Branch '{BranchName}' does not exist. Select 'Create new branch' to create it.");
            }
        }

        if (string.IsNullOrWhiteSpace(WorktreePath))
        {
            errors.Add("Worktree location is required.");
        }
        else if (Directory.Exists(WorktreePath))
        {
            errors.Add($"Directory already exists: {WorktreePath}");
        }

        if (errors.Count > 0)
        {
            ValidationMessage.Text = string.Join("\n", errors);
            ValidationMessage.Visibility = Visibility.Visible;
            CreateButton.IsEnabled = false;
        }
        else
        {
            ValidationMessage.Visibility = Visibility.Collapsed;
            CreateButton.IsEnabled = true;
        }
    }

    #region Event Handlers

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        // Final validation
        ValidateInput();
        if (!CreateButton.IsEnabled)
            return;

        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Worktree Location",
            InitialDirectory = Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath
        };

        if (dialog.ShowDialog() == true)
        {
            // User selected a folder - append the branch name
            var sanitizedBranch = string.IsNullOrWhiteSpace(BranchName)
                ? "worktree"
                : SanitizeBranchForPath(BranchName);

            var repoName = Path.GetFileName(_repoPath);
            WorktreePath = Path.Combine(dialog.FolderName, $"{repoName}.{sanitizedBranch}");
        }
    }

    private void BranchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GitBranch selectedBranch)
        {
            // Set the branch name from the selected branch
            // Use ShortName (e.g., "#95" from "origin/#95") - git worktree will track the remote
            BranchName = selectedBranch.ShortName;

            // Auto-select "Use existing branch" when selecting from dropdown
            UseExistingBranch = true;
            CreateNewBranch = false;
        }
    }

    #endregion
}

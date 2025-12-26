using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TerminalHost.Core.Domain;

namespace TerminalHost.Views.Dialogs;

/// <summary>
/// Dialog for creating a new Timeline intent with options for worktree or existing folder.
/// </summary>
public partial class CreateIntentDialog : Window
{
    private readonly string _repoPath;
    private readonly string _suggestedBasePath;
    private readonly List<GitBranch> _branches;
    private readonly List<string> _openFolders;

    #region Dependency Properties

    public static readonly DependencyProperty IntentNameProperty =
        DependencyProperty.Register(nameof(IntentName), typeof(string), typeof(CreateIntentDialog),
            new PropertyMetadata(string.Empty, OnIntentNameChanged));

    public static readonly DependencyProperty CreateNewWorktreeProperty =
        DependencyProperty.Register(nameof(CreateNewWorktree), typeof(bool), typeof(CreateIntentDialog),
            new PropertyMetadata(false, OnModeChanged));

    public static readonly DependencyProperty UseExistingFolderProperty =
        DependencyProperty.Register(nameof(UseExistingFolder), typeof(bool), typeof(CreateIntentDialog),
            new PropertyMetadata(true, OnModeChanged));

    public static readonly DependencyProperty BranchNameProperty =
        DependencyProperty.Register(nameof(BranchName), typeof(string), typeof(CreateIntentDialog),
            new PropertyMetadata(string.Empty, OnBranchNameChanged));

    public static readonly DependencyProperty WorktreePathProperty =
        DependencyProperty.Register(nameof(WorktreePath), typeof(string), typeof(CreateIntentDialog),
            new PropertyMetadata(string.Empty, OnInputChanged));

    public static readonly DependencyProperty CreateNewBranchProperty =
        DependencyProperty.Register(nameof(CreateNewBranch), typeof(bool), typeof(CreateIntentDialog),
            new PropertyMetadata(true, OnInputChanged));

    public static readonly DependencyProperty UseExistingBranchProperty =
        DependencyProperty.Register(nameof(UseExistingBranch), typeof(bool), typeof(CreateIntentDialog),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ExistingFolderPathProperty =
        DependencyProperty.Register(nameof(ExistingFolderPath), typeof(string), typeof(CreateIntentDialog),
            new PropertyMetadata(string.Empty, OnInputChanged));

    public static readonly DependencyProperty ContextProperty =
        DependencyProperty.Register(nameof(Context), typeof(string), typeof(CreateIntentDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasOpenFoldersProperty =
        DependencyProperty.Register(nameof(HasOpenFolders), typeof(bool), typeof(CreateIntentDialog),
            new PropertyMetadata(false));

    public string IntentName
    {
        get => (string)GetValue(IntentNameProperty);
        set => SetValue(IntentNameProperty, value);
    }

    public bool CreateNewWorktree
    {
        get => (bool)GetValue(CreateNewWorktreeProperty);
        set => SetValue(CreateNewWorktreeProperty, value);
    }

    public bool UseExistingFolder
    {
        get => (bool)GetValue(UseExistingFolderProperty);
        set => SetValue(UseExistingFolderProperty, value);
    }

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

    public string ExistingFolderPath
    {
        get => (string)GetValue(ExistingFolderPathProperty);
        set => SetValue(ExistingFolderPathProperty, value);
    }

    public string Context
    {
        get => (string)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public bool HasOpenFolders
    {
        get => (bool)GetValue(HasOpenFoldersProperty);
        set => SetValue(HasOpenFoldersProperty, value);
    }

    #endregion

    public bool Confirmed { get; private set; }

    public CreateIntentDialog(
        string repoPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath,
        IEnumerable<string> openFolders)
    {
        InitializeComponent();

        _repoPath = repoPath;
        _suggestedBasePath = suggestedBasePath;
        _branches = branches.Where(b => !b.IsCurrent).OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToList();
        _openFolders = openFolders.ToList();

        // Display repo path
        RepoPathDisplay.Text = $"in {Path.GetFileName(_repoPath)}";

        // Populate branch list
        BranchList.ItemsSource = _branches;

        // Populate open folders list
        OpenFoldersList.ItemsSource = _openFolders;
        HasOpenFolders = _openFolders.Count > 0;

        // Default to first open folder if available
        if (_openFolders.Count > 0)
        {
            ExistingFolderPath = _openFolders[0];
        }

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        IntentNameTextBox.Focus();
    }

    private static void OnIntentNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateIntentDialog dialog)
        {
            dialog.UpdateSuggestedBranchName();
            dialog.ValidateInput();
        }
    }

    private static void OnBranchNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateIntentDialog dialog)
        {
            dialog.UpdateSuggestedWorktreePath();
            dialog.ValidateInput();
        }
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateIntentDialog dialog)
        {
            dialog.ValidateInput();
        }
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CreateIntentDialog dialog)
        {
            dialog.ValidateInput();
        }
    }

    private void UpdateSuggestedBranchName()
    {
        if (string.IsNullOrWhiteSpace(IntentName))
            return;

        // Only update if branch name is empty or matches a previous auto-generated name
        if (string.IsNullOrWhiteSpace(BranchName) || BranchName.StartsWith("feature/"))
        {
            BranchName = SuggestBranchName(IntentName);
        }
    }

    private static string SuggestBranchName(string intentName)
    {
        var sanitized = intentName
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        sanitized = new string(sanitized
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());

        while (sanitized.Contains("--"))
            sanitized = sanitized.Replace("--", "-");

        return $"feature/{sanitized.Trim('-')}";
    }

    private void UpdateSuggestedWorktreePath()
    {
        if (string.IsNullOrWhiteSpace(BranchName))
            return;

        var sanitizedBranch = SanitizeBranchForPath(BranchName);
        var repoName = Path.GetFileName(_repoPath);
        var parentDir = Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath;

        WorktreePath = Path.Combine(parentDir, $"{repoName}.{sanitizedBranch}");
    }

    private static string SanitizeBranchForPath(string branchName)
    {
        var sanitized = branchName;
        if (sanitized.StartsWith("feature/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[8..];
        else if (sanitized.StartsWith("bugfix/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[7..];
        else if (sanitized.StartsWith("hotfix/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[7..];
        else if (sanitized.StartsWith("release/", StringComparison.OrdinalIgnoreCase))
            sanitized = sanitized[8..];

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '-');
        }

        sanitized = sanitized.Replace('/', '-').Replace('\\', '-');

        return sanitized;
    }

    private void ValidateInput()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(IntentName))
        {
            errors.Add("Intent name is required.");
        }

        if (CreateNewWorktree)
        {
            if (string.IsNullOrWhiteSpace(BranchName))
            {
                errors.Add("Branch name is required.");
            }
            else if (!CreateNewBranch)
            {
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
        }
        else // UseExistingFolder
        {
            if (string.IsNullOrWhiteSpace(ExistingFolderPath))
            {
                errors.Add("Please select or enter a folder path.");
            }
            else if (!Directory.Exists(ExistingFolderPath))
            {
                errors.Add($"Directory does not exist: {ExistingFolderPath}");
            }
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
        ValidateInput();
        if (!CreateButton.IsEnabled)
            return;

        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void BrowseWorktreeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Worktree Location",
            InitialDirectory = Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath
        };

        if (dialog.ShowDialog() == true)
        {
            var sanitizedBranch = string.IsNullOrWhiteSpace(BranchName)
                ? "worktree"
                : SanitizeBranchForPath(BranchName);

            var repoName = Path.GetFileName(_repoPath);
            WorktreePath = Path.Combine(dialog.FolderName, $"{repoName}.{sanitizedBranch}");
        }
    }

    private void BrowseExistingButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Existing Folder",
            InitialDirectory = _openFolders.Count > 0 ? _openFolders[0] : _repoPath
        };

        if (dialog.ShowDialog() == true)
        {
            ExistingFolderPath = dialog.FolderName;
        }
    }

    private void BranchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GitBranch selectedBranch)
        {
            BranchName = selectedBranch.ShortName;
            UseExistingBranch = true;
            CreateNewBranch = false;
        }
    }

    private void FolderItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is string selectedFolder)
        {
            ExistingFolderPath = selectedFolder;
        }
    }

    #endregion
}

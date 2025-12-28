using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TerminalHost.Domain;

namespace TerminalHost.Views.Dialogs;

public partial class CreateWorktreeDialog : Window
{
    private readonly string _repoPath;
    private readonly string _suggestedBasePath;
    private readonly List<GitBranch> _branches;

    public static readonly StyledProperty<string> BranchNameProperty =
        AvaloniaProperty.Register<CreateWorktreeDialog, string>(nameof(BranchName), string.Empty);

    public static readonly StyledProperty<string> WorktreePathProperty =
        AvaloniaProperty.Register<CreateWorktreeDialog, string>(nameof(WorktreePath), string.Empty);

    public static readonly StyledProperty<bool> CreateNewBranchProperty =
        AvaloniaProperty.Register<CreateWorktreeDialog, bool>(nameof(CreateNewBranch), true);

    public static readonly StyledProperty<bool> UseExistingBranchProperty =
        AvaloniaProperty.Register<CreateWorktreeDialog, bool>(nameof(UseExistingBranch), false);

    public static readonly StyledProperty<bool> OpenAfterCreationProperty =
        AvaloniaProperty.Register<CreateWorktreeDialog, bool>(nameof(OpenAfterCreation), true);

    public string BranchName
    {
        get => GetValue(BranchNameProperty);
        set => SetValue(BranchNameProperty, value);
    }

    public string WorktreePath
    {
        get => GetValue(WorktreePathProperty);
        set => SetValue(WorktreePathProperty, value);
    }

    public bool CreateNewBranch
    {
        get => GetValue(CreateNewBranchProperty);
        set => SetValue(CreateNewBranchProperty, value);
    }

    public bool UseExistingBranch
    {
        get => GetValue(UseExistingBranchProperty);
        set => SetValue(UseExistingBranchProperty, value);
    }

    public bool OpenAfterCreation
    {
        get => GetValue(OpenAfterCreationProperty);
        set => SetValue(OpenAfterCreationProperty, value);
    }

    public bool Confirmed { get; private set; }

    public CreateWorktreeDialog()
    {
        InitializeComponent();
        _repoPath = "";
        _suggestedBasePath = "";
        _branches = [];
    }

    public CreateWorktreeDialog(string repoPath, IEnumerable<GitBranch> branches, string suggestedBasePath)
    {
        InitializeComponent();

        _repoPath = repoPath;
        _suggestedBasePath = suggestedBasePath;
        // Sort: local branches first (by SortOrder), then by name, exclude current branch
        _branches = branches.Where(b => !b.IsCurrent).OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToList();

        // Populate branch list
        BranchList.ItemsSource = _branches;

        // Set initial suggested path hint
        UpdateSuggestedPathHint();

        // Subscribe to property changes
        this.GetObservable(BranchNameProperty).Subscribe(_ => OnBranchNameChanged());
        this.GetObservable(CreateNewBranchProperty).Subscribe(_ => ValidateInput());

        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        BranchTextBox.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && CreateButton.IsEnabled)
        {
            CreateButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void OnBranchNameChanged()
    {
        UpdateSuggestedPath();
        UpdateSuggestedPathHint();
        ValidateInput();
    }

    private void UpdateSuggestedPath()
    {
        if (string.IsNullOrWhiteSpace(BranchName))
            return;

        // Auto-generate path: basePath-branchName (sanitized)
        var sanitizedBranch = SanitizeBranchForPath(BranchName);
        var repoName = Path.GetFileName(_repoPath.TrimEnd(Path.DirectorySeparatorChar));
        var parentDir = Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath;

        WorktreePath = Path.Combine(parentDir, $"{repoName}.{sanitizedBranch}");
    }

    private void UpdateSuggestedPathHint()
    {
        var repoName = Path.GetFileName(_repoPath.TrimEnd(Path.DirectorySeparatorChar));
        SuggestedPathHint.Text = $"Suggested: {{parent-folder}}/{repoName}.{{branch}}";
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
            var exists = _branches.Any(b =>
                b.ShortName.Equals(BranchName, StringComparison.OrdinalIgnoreCase) ||
                b.Name.Equals(BranchName, StringComparison.OrdinalIgnoreCase));

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
            ValidationMessage.IsVisible = true;
            CreateButton.IsEnabled = false;
        }
        else
        {
            ValidationMessage.IsVisible = false;
            CreateButton.IsEnabled = true;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        // Final validation
        ValidateInput();
        if (!CreateButton.IsEnabled)
            return;

        Confirmed = true;
        Close(true);
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var startLocation = await storage.TryGetFolderFromPathAsync(
            Path.GetDirectoryName(_suggestedBasePath) ?? _suggestedBasePath);

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Worktree Location",
            SuggestedStartLocation = startLocation,
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var selectedFolder = result[0].Path.LocalPath;
            var sanitizedBranch = string.IsNullOrWhiteSpace(BranchName)
                ? "worktree"
                : SanitizeBranchForPath(BranchName);

            var repoName = Path.GetFileName(_repoPath.TrimEnd(Path.DirectorySeparatorChar));
            WorktreePath = Path.Combine(selectedFolder, $"{repoName}.{sanitizedBranch}");
        }
    }

    private void BranchItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: GitBranch selectedBranch })
            return;

        // Set the branch name from the selected branch
        // Use ShortName (e.g., "#95" from "origin/#95") - git worktree will track the remote
        BranchName = selectedBranch.ShortName;

        // Auto-select "Use existing branch" when selecting from dropdown
        UseExistingBranch = true;
        CreateNewBranch = false;
    }

    public CreateWorktreeDialogResult? GetResult()
    {
        if (!Confirmed)
            return null;

        return new CreateWorktreeDialogResult(
            BranchName,
            WorktreePath,
            CreateNewBranch,
            OpenAfterCreation);
    }
}

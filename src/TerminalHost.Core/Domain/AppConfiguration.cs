using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Defines the layout mode for the terminal pair view.
/// </summary>
public enum TerminalLayoutMode
{
    /// <summary>
    /// Only the Custom terminal is visible (full width/height).
    /// </summary>
    CustomFull,

    /// <summary>
    /// Both terminals displayed side by side (horizontal split).
    /// </summary>
    HorizontalSplit,

    /// <summary>
    /// Both terminals stacked vertically (vertical split).
    /// </summary>
    VerticalSplit
}

/// <summary>
/// Defines the sort mode for workspaces in the sidebar.
/// </summary>
public enum WorkspaceSortMode
{
    /// <summary>
    /// Manual order based on user arrangement.
    /// </summary>
    Manual,

    /// <summary>
    /// Sorted by usage (focus time + character count) over the last 7 days.
    /// </summary>
    Usage,

    /// <summary>
    /// Sorted alphabetically by name.
    /// </summary>
    Alphabetical
}

/// <summary>
/// Defines behavior when launching the app while another instance is running.
/// </summary>
public enum SingleInstanceBehavior
{
    /// <summary>
    /// Show a dialog offering Focus Existing, Open New Instance, or Cancel.
    /// </summary>
    ShowDialog,

    /// <summary>
    /// Silently focus the existing instance without showing any dialog.
    /// </summary>
    SilentFocus,

    /// <summary>
    /// Always allow multiple instances without checking.
    /// </summary>
    AllowMultiple
}

public class AppConfiguration
{
    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = [];

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();

    [JsonPropertyName("windowState")]
    public WindowStateInfo WindowState { get; set; } = new();

    [JsonPropertyName("openFolders")]
    public List<string> OpenFolders { get; set; } = [];

    /// <summary>
    /// The last selected/active folder tab (to restore on restart).
    /// </summary>
    [JsonPropertyName("lastSelectedFolder")]
    public string? LastSelectedFolder { get; set; }

    /// <summary>
    /// The type of tab that was last selected (Project, Dashboard, Timeline, etc.).
    /// Used to restore special tabs as the active tab on restart.
    /// </summary>
    [JsonPropertyName("lastSelectedTabType")]
    public string? LastSelectedTabType { get; set; }

    [JsonPropertyName("directorySettings")]
    public Dictionary<string, DirectorySettings> DirectorySettings { get; set; } = [];

    [JsonPropertyName("quickCommands")]
    public List<QuickCommand> QuickCommands { get; set; } = GetDefaultQuickCommands();

    [JsonPropertyName("linkPatterns")]
    public List<LinkPattern> LinkPatterns { get; set; } = GetDefaultLinkPatterns();

    [JsonPropertyName("scratchPads")]
    public Dictionary<string, string> ScratchPads { get; set; } = [];  // Directory path -> content

    [JsonPropertyName("globalScratchPad")]
    public string GlobalScratchPad { get; set; } = "";

    [JsonPropertyName("projectTypes")]
    public List<ProjectType> ProjectTypes { get; set; } = ProjectType.GetDefaults();

    /// <summary>
    /// Most recently used command IDs for Command Palette sorting.
    /// </summary>
    [JsonPropertyName("commandPaletteMru")]
    public List<string> CommandPaletteMru { get; set; } = [];

    /// <summary>
    /// Workspace entries for the sidebar layout mode.
    /// </summary>
    [JsonPropertyName("workspaces")]
    public List<Workspace> Workspaces { get; set; } = [];

    /// <summary>
    /// Configured AI assistants (Claude, Gemini, Codex, Copilot, custom).
    /// </summary>
    [JsonPropertyName("aiAssistants")]
    public List<AiAssistant> AiAssistants { get; set; } = [];

    /// <summary>
    /// Timeline IDE state (intents, sessions, focus time).
    /// </summary>
    [JsonPropertyName("timelineState")]
    public TimelineState? TimelineState { get; set; }

    /// <summary>
    /// Focus mode tasks (hierarchical task list).
    /// </summary>
    [JsonPropertyName("tasks")]
    public List<FocusTask> Tasks { get; set; } = [];

    /// <summary>
    /// Quick notes for tasks.
    /// </summary>
    [JsonPropertyName("quickNotes")]
    public List<QuickNote> QuickNotes { get; set; } = [];

    /// <summary>
    /// Focus mode state (enabled, current task, etc.).
    /// </summary>
    [JsonPropertyName("focusMode")]
    public FocusModeState? FocusMode { get; set; }

    /// <summary>
    /// Determines if this configuration is in its default/untouched state.
    /// Used for first-run detection.
    /// </summary>
    public bool IsDefault()
    {
        // Already completed first run - not default
        if (Settings.FirstRunCompleted) return false;

        // Has open folders history - user has used the app
        if (OpenFolders.Count > 0) return false;

        // Has custom scratch pad content
        if (!string.IsNullOrEmpty(GlobalScratchPad)) return false;
        if (ScratchPads.Count > 0) return false;

        // Has custom profiles (beyond default PowerShell)
        if (Profiles.Count > 1) return false;
        if (Profiles.Count == 1 && Profiles[0].Id != "powershell") return false;

        // Has directory-specific settings
        if (DirectorySettings.Count > 0) return false;

        // Has command palette history
        if (CommandPaletteMru.Count > 0) return false;

        return true;
    }

    private static List<LinkPattern> GetDefaultLinkPatterns() =>
    [
        // Example pattern - users can customize or add their own
        // Uncomment and modify for your ticketing system:
        // new LinkPattern
        // {
        //     Id = "jira-ticket",
        //     Name = "JIRA Ticket",
        //     Pattern = @"([A-Z]+-\d+)",
        //     UrlTemplate = "https://jira.example.com/browse/$1",
        //     Enabled = true,
        //     Priority = 10
        // }
    ];

    private static List<QuickCommand> GetDefaultQuickCommands() =>
    [
        // Claude Code commands
        new QuickCommand
        {
            Id = "commit",
            Label = "Commit",
            Icon = "💾",
            Text = "commit",
            Target = QuickCommandTarget.Custom,
            AppendNewline = true,
            UseUserInput = true,
            Shortcut = "Ctrl+Shift+C"
        },
        new QuickCommand
        {
            Id = "rate-code",
            Label = "Rate Code",
            Icon = "⭐",
            Text = "rate my code - give me a thorough code review with ratings and suggestions",
            Target = QuickCommandTarget.Custom,
            AppendNewline = true,
            UseUserInput = true,
            Shortcut = ""  // No default shortcut (Ctrl+Shift+R used by PR Review Mode)
        },
        new QuickCommand
        {
            Id = "review-pr",
            Label = "Review PR",
            Icon = "🔍",
            Text = "review the current PR - check the git diff and provide feedback",
            Target = QuickCommandTarget.Custom,
            AppendNewline = true,
            UseUserInput = true,
            Shortcut = "Ctrl+Shift+V"
        },
        // Git pull/push are now toolbar buttons (not quick commands)
        // Dev tool commands
        new QuickCommand
        {
            Id = "dev-launch",
            Label = "Launch IDE",
            Icon = "▶",
            Text = "dev",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = "Ctrl+Shift+L"
        },
        new QuickCommand
        {
            Id = "dev-build",
            Label = "Build",
            Icon = "b",
            Text = "dev b",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = "Ctrl+Shift+B"
        },
        new QuickCommand
        {
            Id = "dev-version-commit",
            Label = "Version+Commit",
            Icon = "vc",
            Text = "dev vc",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            UseUserInput = true,  // To select major/minor/patch/revision
            Shortcut = ""
        },
        new QuickCommand
        {
            Id = "dev-clean",
            Label = "Clean",
            Icon = "c",
            Text = "dev c",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = ""
        },
        new QuickCommand
        {
            Id = "dev-frontend",
            Label = "Frontend",
            Icon = "f",
            Text = "dev f",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = ""
        }
    ];
}

public class DirectorySettings
{
    [JsonPropertyName("layoutMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TerminalLayoutMode LayoutMode { get; set; } = TerminalLayoutMode.HorizontalSplit;

    [JsonPropertyName("splitRatio")]
    public double SplitRatio { get; set; } = 0.6;  // Custom terminal takes 60% by default

    [JsonPropertyName("activeTerminal")]
    public string ActiveTerminal { get; set; } = "Custom";  // "Custom", "Shell", or "Run"

    // Run terminal settings
    [JsonPropertyName("runConfigurations")]
    public List<RunConfiguration> RunConfigurations { get; set; } = [];

    [JsonPropertyName("isRunTerminalVisible")]
    public bool IsRunTerminalVisible { get; set; } = false;

    [JsonPropertyName("runSplitRatio")]
    public double RunSplitRatio { get; set; } = 0.3;  // Run terminal takes 30% by default

    [JsonPropertyName("activeRunConfigurationId")]
    public string? ActiveRunConfigurationId { get; set; }

    [JsonPropertyName("detectedProjectType")]
    public string? DetectedProjectType { get; set; }  // Cached project type ID

    // Explorer settings
    [JsonPropertyName("isExplorerVisible")]
    public bool IsExplorerVisible { get; set; } = false;

    [JsonPropertyName("explorerSplitRatio")]
    public double ExplorerSplitRatio { get; set; } = 0.25;  // Explorer takes 25% by default

    /// <summary>
    /// ID of the active AI assistant for this directory.
    /// If null, uses the default assistant.
    /// </summary>
    [JsonPropertyName("activeAiAssistantId")]
    public string? ActiveAiAssistantId { get; set; }

    // Panel system settings
    /// <summary>
    /// Per-panel state configuration (keyed by panel ID).
    /// </summary>
    [JsonPropertyName("panelStates")]
    public Dictionary<string, PanelStateConfig> PanelStates { get; set; } = [];

    /// <summary>
    /// ID of the active panel tab in the right panel host.
    /// </summary>
    [JsonPropertyName("activeRightPanel")]
    public string? ActiveRightPanel { get; set; }

    /// <summary>
    /// ID of the active panel tab in the left panel host.
    /// </summary>
    [JsonPropertyName("activeLeftPanel")]
    public string? ActiveLeftPanel { get; set; }

    /// <summary>
    /// Width of the left panel host (as a ratio of total width).
    /// </summary>
    [JsonPropertyName("leftPanelSplitRatio")]
    public double LeftPanelSplitRatio { get; set; } = 0.25;

    /// <summary>
    /// Whether the left panel host is visible.
    /// </summary>
    [JsonPropertyName("isLeftPanelVisible")]
    public bool IsLeftPanelVisible { get; set; } = false;

    /// <summary>
    /// Per-project override for key branches.
    /// If set, these branch names are used instead of the global KeyBranches setting.
    /// This is useful for projects that use different branch naming conventions.
    /// </summary>
    [JsonPropertyName("keyBranchOverrides")]
    public List<string>? KeyBranchOverrides { get; set; }

    // Center panel settings
    /// <summary>
    /// ID of the active center panel (null = terminals visible).
    /// </summary>
    [JsonPropertyName("activeCenterPanel")]
    public string? ActiveCenterPanel { get; set; }

    /// <summary>
    /// Panel IDs that are open in the right sidebar.
    /// </summary>
    [JsonPropertyName("openRightPanels")]
    public List<string> OpenRightPanels { get; set; } = [];

    /// <summary>
    /// Active tab in the git panel (persisted across restarts).
    /// </summary>
    [JsonPropertyName("gitPanelActiveTab")]
    public string? GitPanelActiveTab { get; set; }

    // Git panel settings
    /// <summary>
    /// Saved width of the unified Git panel popup.
    /// </summary>
    [JsonPropertyName("gitPanelWidth")]
    public double? GitPanelWidth { get; set; }

    /// <summary>
    /// Saved height of the unified Git panel popup.
    /// </summary>
    [JsonPropertyName("gitPanelHeight")]
    public double? GitPanelHeight { get; set; }

    /// <summary>
    /// Whether compact view is enabled in commit history.
    /// </summary>
    [JsonPropertyName("isCompactHistoryView")]
    public bool IsCompactHistoryView { get; set; }

    /// <summary>
    /// Whether tree view is enabled in git changes panel.
    /// </summary>
    [JsonPropertyName("isGitFilesTreeView")]
    public bool IsGitFilesTreeView { get; set; }

    /// <summary>
    /// Whether tree view is enabled in git branches panel.
    /// </summary>
    [JsonPropertyName("isGitBranchTreeView")]
    public bool IsGitBranchTreeView { get; set; }
}

/// <summary>
/// Configuration for a single panel's state within a directory.
/// </summary>
public class PanelStateConfig
{
    /// <summary>
    /// Whether this panel is currently docked (true) or in popup/window state (false).
    /// </summary>
    [JsonPropertyName("isDocked")]
    public bool IsDocked { get; set; }

    /// <summary>
    /// Which side the panel is docked to when in Panel state.
    /// </summary>
    [JsonPropertyName("side")]
    public string Side { get; set; } = "Right";
}

public class WindowStateInfo
{
    [JsonPropertyName("left")]
    public double Left { get; set; } = 100;

    [JsonPropertyName("top")]
    public double Top { get; set; } = 100;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 1200;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 800;

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; } = false;
}

public class AppSettings
{
    [JsonPropertyName("confirmOnClose")]
    public bool ConfirmOnClose { get; set; } = true;

    [JsonPropertyName("showInSystemTray")]
    public bool ShowInSystemTray { get; set; } = false;

    [JsonPropertyName("customCommand")]
    public string CustomCommand { get; set; } = @"%USERPROFILE%\.local\bin\claude.exe";

    [JsonPropertyName("customCommandName")]
    public string CustomCommandName { get; set; } = "Claude Code";

    [JsonPropertyName("customCommandIcon")]
    public string CustomCommandIcon { get; set; } = "🤖";

    [JsonPropertyName("shellCommand")]
    public string ShellCommand { get; set; } = "pwsh.exe";

    [JsonPropertyName("shellCommandName")]
    public string ShellCommandName { get; set; } = "PowerShell";

    [JsonPropertyName("shellCommandIcon")]
    public string ShellCommandIcon { get; set; } = "💻";

    /// <summary>
    /// Keyboard shortcuts for Claude commands (command name -> shortcut string).
    /// Example: { "review-pr": "Ctrl+Alt+R" }
    /// </summary>
    [JsonPropertyName("claudeCommandShortcuts")]
    public Dictionary<string, string> ClaudeCommandShortcuts { get; set; } = [];

    /// <summary>
    /// GitHub Dashboard settings.
    /// </summary>
    [JsonPropertyName("dashboard")]
    public DashboardSettings Dashboard { get; set; } = new();

    /// <summary>
    /// Timeline Mode settings.
    /// </summary>
    [JsonPropertyName("timeline")]
    public TimelineSettings Timeline { get; set; } = new();

    /// <summary>
    /// Repository quick access settings.
    /// </summary>
    [JsonPropertyName("repositories")]
    public RepositorySettings Repositories { get; set; } = new();

    /// <summary>
    /// Test runner settings.
    /// </summary>
    [JsonPropertyName("testing")]
    public TestingSettings Testing { get; set; } = new();

    /// <summary>
    /// Markdown preview settings.
    /// </summary>
    [JsonPropertyName("markdown")]
    public MarkdownSettings Markdown { get; set; } = new();

    /// <summary>
    /// Behavior when launching without arguments while an instance is running.
    /// </summary>
    [JsonPropertyName("singleInstanceBehavior")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SingleInstanceBehavior SingleInstanceBehavior { get; set; } = SingleInstanceBehavior.ShowDialog;

    /// <summary>
    /// Whether to allow multiple tabs for the same folder.
    /// </summary>
    [JsonPropertyName("allowDuplicateTabs")]
    public bool AllowDuplicateTabs { get; set; } = true;

    /// <summary>
    /// Whether the first-run setup has been completed.
    /// </summary>
    [JsonPropertyName("firstRunCompleted")]
    public bool FirstRunCompleted { get; set; } = false;

    /// <summary>
    /// The date/time when first-run setup was completed.
    /// </summary>
    [JsonPropertyName("firstRunDate")]
    public DateTime? FirstRunDate { get; set; } = null;

    /// <summary>
    /// Application-wide layout mode for displaying projects.
    /// </summary>
    [JsonPropertyName("layoutMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLayoutMode LayoutMode { get; set; } = AppLayoutMode.Tabs;

    /// <summary>
    /// Width of the workspace sidebar in pixels.
    /// </summary>
    [JsonPropertyName("sidebarWidth")]
    public double SidebarWidth { get; set; } = 250;

    /// <summary>
    /// Whether the workspace sidebar is collapsed.
    /// </summary>
    [JsonPropertyName("sidebarCollapsed")]
    public bool SidebarCollapsed { get; set; } = false;

    /// <summary>
    /// Whether to automatically fetch from git remotes periodically.
    /// </summary>
    [JsonPropertyName("gitAutoFetch")]
    public bool GitAutoFetch { get; set; } = true;

    /// <summary>
    /// Interval in seconds between automatic git fetches (default: 60 seconds).
    /// </summary>
    [JsonPropertyName("gitAutoFetchIntervalSeconds")]
    public int GitAutoFetchIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to show stash count in the workspace sidebar.
    /// </summary>
    [JsonPropertyName("showStashCount")]
    public bool ShowStashCount { get; set; } = true;

    /// <summary>
    /// Sort mode for workspaces in the sidebar.
    /// </summary>
    [JsonPropertyName("workspaceSortMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkspaceSortMode WorkspaceSortMode { get; set; } = WorkspaceSortMode.Manual;

    /// <summary>
    /// Filter text for workspaces in the sidebar.
    /// </summary>
    [JsonPropertyName("workspaceFilterText")]
    public string WorkspaceFilterText { get; set; } = "";

    /// <summary>
    /// Whether touch-friendly mode is enabled (larger touch targets, more padding).
    /// Useful for remote desktop from mobile devices.
    /// </summary>
    [JsonPropertyName("touchMode")]
    public bool TouchMode { get; set; } = false;

    /// <summary>
    /// Branch names to track for quick access in Git operations.
    /// These are auto-detected in each repo, matching by short name.
    /// </summary>
    [JsonPropertyName("keyBranches")]
    public List<string> KeyBranches { get; set; } = GetDefaultKeyBranches();

    /// <summary>
    /// Custom paths to add to PATH environment variable.
    /// </summary>
    [JsonPropertyName("customPaths")]
    public List<string> CustomPaths { get; set; } = [];

    /// <summary>
    /// Input prompt detection settings (for "waiting for input" indicator).
    /// </summary>
    [JsonPropertyName("inputPrompt")]
    public InputPromptSettings InputPrompt { get; set; } = new();

    /// <summary>
    /// Sound notification settings.
    /// </summary>
    [JsonPropertyName("sounds")]
    public SoundSettings Sounds { get; set; } = new();

    private static List<string> GetDefaultKeyBranches() =>
    [
        "main",
        "master",
        "development",
        "develop",
        "dev",
        "production",
        "prod",
        "staging",
        "stage",
        "qa"
    ];
}

/// <summary>
/// Settings for the GitHub Dashboard feature.
/// </summary>
public class DashboardSettings
{
    /// <summary>
    /// Whether the dashboard feature is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often to refresh dashboard data (in minutes).
    /// </summary>
    [JsonPropertyName("refreshIntervalMinutes")]
    public int RefreshIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// GitHub organizations to watch for PRs/issues.
    /// If empty, watches all accessible repos.
    /// </summary>
    [JsonPropertyName("watchedOrgs")]
    public List<string> WatchedOrgs { get; set; } = [];

    /// <summary>
    /// Repositories to exclude from the dashboard.
    /// </summary>
    [JsonPropertyName("excludedRepos")]
    public List<string> ExcludedRepos { get; set; } = [];

    /// <summary>
    /// Whether to show CI/workflow status.
    /// </summary>
    [JsonPropertyName("showCIStatus")]
    public bool ShowCIStatus { get; set; } = true;

    /// <summary>
    /// Whether to show the dashboard tab on startup.
    /// </summary>
    [JsonPropertyName("showOnStartup")]
    public bool ShowOnStartup { get; set; } = false;
}

/// <summary>
/// Settings for the Timeline Mode feature.
/// </summary>
public class TimelineSettings
{
    /// <summary>
    /// Whether Timeline Mode is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to show the Timeline tab on startup.
    /// </summary>
    [JsonPropertyName("showOnStartup")]
    public bool ShowOnStartup { get; set; } = false;

    /// <summary>
    /// Whether Claude Code hooks are installed for session tracking.
    /// </summary>
    [JsonPropertyName("hooksInstalled")]
    public bool HooksInstalled { get; set; } = false;

    /// <summary>
    /// Whether to track sessions that don't match any Intent.
    /// </summary>
    [JsonPropertyName("trackUnassignedSessions")]
    public bool TrackUnassignedSessions { get; set; } = true;
}

/// <summary>
/// Settings for the Repository Quick Access feature.
/// </summary>
public class RepositorySettings
{
    /// <summary>
    /// Directories to scan for git repositories.
    /// </summary>
    [JsonPropertyName("scanPaths")]
    public List<string> ScanPaths { get; set; } = [];

    /// <summary>
    /// Favorite repositories (full names like "owner/repo").
    /// </summary>
    [JsonPropertyName("favorites")]
    public List<string> Favorites { get; set; } = [];

    /// <summary>
    /// Default directory for cloning new repositories.
    /// </summary>
    [JsonPropertyName("cloneDirectory")]
    public string CloneDirectory { get; set; } = "";

    /// <summary>
    /// Recently accessed repositories (paths).
    /// </summary>
    [JsonPropertyName("recentPaths")]
    public List<string> RecentPaths { get; set; } = [];

    /// <summary>
    /// Maximum number of recent repositories to track.
    /// </summary>
    [JsonPropertyName("maxRecentItems")]
    public int MaxRecentItems { get; set; } = 20;
}

/// <summary>
/// Settings for the Test Runner feature.
/// </summary>
public class TestingSettings
{
    /// <summary>
    /// Whether to run tests automatically on file save.
    /// </summary>
    [JsonPropertyName("runOnSave")]
    public bool RunOnSave { get; set; } = false;

    /// <summary>
    /// Whether to show the test results panel automatically.
    /// </summary>
    [JsonPropertyName("showResultsPanel")]
    public bool ShowResultsPanel { get; set; } = true;

    /// <summary>
    /// Whether to automatically focus the results panel on test failure.
    /// </summary>
    [JsonPropertyName("autoFocusOnFailure")]
    public bool AutoFocusOnFailure { get; set; } = true;

    /// <summary>
    /// Default test command override (if not auto-detected).
    /// </summary>
    [JsonPropertyName("defaultTestCommand")]
    public string? DefaultTestCommand { get; set; }
}

/// <summary>
/// Settings for the Markdown Preview feature.
/// </summary>
public class MarkdownSettings
{
    /// <summary>
    /// Whether to auto-reload markdown files when they change.
    /// </summary>
    [JsonPropertyName("autoReload")]
    public bool AutoReload { get; set; } = true;

    /// <summary>
    /// Default panel position: "right" or "bottom".
    /// </summary>
    [JsonPropertyName("defaultPanelPosition")]
    public string DefaultPanelPosition { get; set; } = "right";

    /// <summary>
    /// Whether to sync scroll position with editor.
    /// </summary>
    [JsonPropertyName("syncScroll")]
    public bool SyncScroll { get; set; } = true;
}

/// <summary>
/// Settings for sound notifications.
/// </summary>
public class SoundSettings
{
    /// <summary>
    /// Whether sound notifications are enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Only play sounds when the application window is not focused.
    /// </summary>
    [JsonPropertyName("onlyWhenUnfocused")]
    public bool OnlyWhenUnfocused { get; set; } = true;

    /// <summary>
    /// Sound to play on successful completion (toast success).
    /// Can be a system sound name or path to a .wav file.
    /// System sounds: Asterisk, Beep, Exclamation, Hand, Question
    /// </summary>
    [JsonPropertyName("successSound")]
    public string SuccessSound { get; set; } = "Asterisk";

    /// <summary>
    /// Sound to play on error/failure (toast error).
    /// </summary>
    [JsonPropertyName("errorSound")]
    public string ErrorSound { get; set; } = "Hand";

    /// <summary>
    /// Sound to play on warning notifications.
    /// </summary>
    [JsonPropertyName("warningSound")]
    public string WarningSound { get; set; } = "Exclamation";

    /// <summary>
    /// Sound to play on informational notifications.
    /// </summary>
    [JsonPropertyName("infoSound")]
    public string InfoSound { get; set; } = "";

    /// <summary>
    /// Sound to play when terminal is waiting for input (requires input prompt detection).
    /// </summary>
    [JsonPropertyName("inputWaitingSound")]
    public string InputWaitingSound { get; set; } = "Exclamation";

    /// <summary>
    /// Volume level (0.0 to 1.0). Only applies to custom .wav files.
    /// </summary>
    [JsonPropertyName("volume")]
    public double Volume { get; set; } = 1.0;
}

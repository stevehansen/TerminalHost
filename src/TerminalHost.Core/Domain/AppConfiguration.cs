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
    /// Focus mode state (enabled, current task, history).
    /// </summary>
    [JsonPropertyName("focusMode")]
    public FocusModeState FocusMode { get; set; } = new();

    /// <summary>
    /// All focus tasks.
    /// </summary>
    [JsonPropertyName("tasks")]
    public List<FocusTask> Tasks { get; set; } = [];

    /// <summary>
    /// Quick notes (not yet converted to tasks).
    /// </summary>
    [JsonPropertyName("quickNotes")]
    public List<QuickNote> QuickNotes { get; set; } = [];

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

        // Has tasks
        if (Tasks.Count > 0) return false;

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
        // Git commands
        new QuickCommand
        {
            Id = "git-pull",
            Label = "Pull",
            Icon = "↓",
            Text = "git pull --rebase",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = "Ctrl+Shift+D"  // D for Download
        },
        new QuickCommand
        {
            Id = "git-push",
            Label = "Push",
            Icon = "↑",
            Text = "git push",
            Target = QuickCommandTarget.Shell,
            AppendNewline = true,
            Shortcut = "Ctrl+Shift+U"  // U for Upload
        },
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
            Shortcut = ""  // No default shortcut (Ctrl+Shift+B used by File Blame)
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
    /// Whether to auto-sort workspaces in the sidebar by recent usage.
    /// </summary>
    [JsonPropertyName("workspaceAutoSort")]
    public bool WorkspaceAutoSort { get; set; } = false;
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

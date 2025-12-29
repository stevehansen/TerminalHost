using System.IO;
using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

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
    /// Configured AI assistants (Claude, Gemini, Codex, Copilot, custom).
    /// </summary>
    [JsonPropertyName("aiAssistants")]
    public List<AiAssistant> AiAssistants { get; set; } = [];

    /// <summary>
    /// Timeline Mode state (intents, sessions, focus time).
    /// </summary>
    [JsonPropertyName("timelineState")]
    public TimelineState? TimelineState { get; set; }

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
            Shortcut = "Ctrl+Shift+R"
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
    public string CustomCommand { get; set; } = GetDefaultClaudeCommand();

    [JsonPropertyName("customCommandName")]
    public string CustomCommandName { get; set; } = "Claude Code";

    [JsonPropertyName("customCommandIcon")]
    public string CustomCommandIcon { get; set; } = "🤖";

    [JsonPropertyName("shellCommand")]
    public string ShellCommand { get; set; } = GetDefaultShell();

    [JsonPropertyName("shellCommandName")]
    public string ShellCommandName { get; set; } = GetDefaultShellName();

    [JsonPropertyName("shellCommandIcon")]
    public string ShellCommandIcon { get; set; } = "💻";

    private static string GetDefaultClaudeCommand()
    {
        // macOS: ~/.local/bin/claude
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudePath = Path.Combine(home, ".local", "bin", "claude");
        if (File.Exists(claudePath))
            return claudePath;

        // Fallback to just "claude" in PATH
        return "claude";
    }

    private static string GetDefaultShell()
    {
        // Check for environment variable first
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        // macOS defaults
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        if (File.Exists("/bin/bash")) return "/bin/bash";

        return "/bin/sh";
    }

    private static string GetDefaultShellName()
    {
        var shell = GetDefaultShell();
        return Path.GetFileName(shell) switch
        {
            "zsh" => "Zsh",
            "bash" => "Bash",
            "fish" => "Fish",
            _ => "Shell"
        };
    }

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
    /// Custom paths to add to the terminal PATH environment variable.
    /// These are added before the default paths.
    /// Example: "/usr/local/share/dotnet" for .NET SDK
    /// </summary>
    [JsonPropertyName("customPaths")]
    public List<string> CustomPaths { get; set; } = [];

    /// <summary>
    /// Application layout mode (Tabs or Sidebar).
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
    /// Recent workspaces for quick access in sidebar.
    /// </summary>
    [JsonPropertyName("recentWorkspaces")]
    public List<Workspace> RecentWorkspaces { get; set; } = [];

    /// <summary>
    /// Maximum number of recent workspaces to track.
    /// </summary>
    [JsonPropertyName("maxRecentWorkspaces")]
    public int MaxRecentWorkspaces { get; set; } = 20;

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

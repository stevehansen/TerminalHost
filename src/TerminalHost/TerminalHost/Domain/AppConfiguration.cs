using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

public class AppConfiguration
{
    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();

    [JsonPropertyName("windowState")]
    public WindowStateInfo WindowState { get; set; } = new();

    [JsonPropertyName("openFolders")]
    public List<string> OpenFolders { get; set; } = new();

    [JsonPropertyName("directorySettings")]
    public Dictionary<string, DirectorySettings> DirectorySettings { get; set; } = new();

    [JsonPropertyName("quickCommands")]
    public List<QuickCommand> QuickCommands { get; set; } = GetDefaultQuickCommands();

    private static List<QuickCommand> GetDefaultQuickCommands() =>
    [
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
            Id = "git-pull",
            Label = "Pull",
            Icon = "↓",
            Text = "git pull",
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
        }
    ];
}

public class DirectorySettings
{
    [JsonPropertyName("isSplitView")]
    public bool IsSplitView { get; set; } = true;

    [JsonPropertyName("splitRatio")]
    public double SplitRatio { get; set; } = 0.6;  // Custom terminal takes 60% by default

    [JsonPropertyName("activeTerminal")]
    public string ActiveTerminal { get; set; } = "Custom";  // "Custom" or "Shell"
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
    public string CustomCommand { get; set; } = @"C:\Users\Administrator\.local\bin\claude.exe";

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
}

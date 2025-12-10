using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

public class AppConfiguration
{
    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();
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

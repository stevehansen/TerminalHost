using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.Core.Domain;

public partial class Dependency : ObservableObject
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? InstallCommand { get; init; }
    public string? InstallUrl { get; init; }
    public required string DetectionCommand { get; init; }
    public required string HomepageUrl { get; init; }

    /// <summary>
    /// If set, the detection is considered successful if the output contains this string
    /// (case-insensitive). Used for commands like "gh extension list" where we need to
    /// check if a specific extension is in the output.
    /// </summary>
    public string? DetectionOutputContains { get; init; }

    /// <summary>
    /// Whether this dependency is an AI assistant (Claude, Gemini, etc.).
    /// </summary>
    public bool IsAiAssistant { get; init; }

    /// <summary>
    /// The ID of the AI assistant in the AiAssistants config (e.g., "claude", "gemini").
    /// </summary>
    public string? AiAssistantId { get; init; }

    public string InstallToolTipText =>
        !string.IsNullOrEmpty(InstallCommand)
            ? $"Run: {InstallCommand}"
            : !string.IsNullOrEmpty(HomepageUrl)
                ? $"Open: {HomepageUrl}"
                : "No installation method available.";

    // Properties for the UI to bind to
    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isDetecting;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _detectedVersion;

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private int _exitCode;

    [ObservableProperty]
    private string? _fullOutput;
}

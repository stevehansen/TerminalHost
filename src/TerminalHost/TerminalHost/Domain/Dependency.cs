using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.Domain;

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

    /// <summary>
    /// Returns "Install" or "Homepage" based on install status and available commands.
    /// Shows "Homepage" if installed OR if there's no InstallCommand.
    /// </summary>
    public string InstallButtonText =>
        IsInstalled || string.IsNullOrEmpty(InstallCommand)
            ? "Homepage"
            : "Install";

    /// <summary>
    /// Returns the status icon based on detection state.
    /// </summary>
    public string StatusIcon =>
        IsDetecting ? "◌" :
        IsInstalled ? "●" : "●";

    /// <summary>
    /// Returns the status color based on detection state.
    /// </summary>
    public string StatusColor =>
        IsDetecting ? "#9CDCFE" :  // SyntaxLightBlueBrush
        IsInstalled ? "#B5CEA8" :  // SyntaxGreenBrush
        "#F48771";                  // StatusErrorBrush

    // Properties for the UI to bind to
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(IsNotInstalling))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private bool _isDetecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInstalling))]
    private bool _isInstalling;

    /// <summary>
    /// Inverse of IsInstalling, for binding to IsEnabled.
    /// </summary>
    public bool IsNotInstalling => !IsInstalling;

    [ObservableProperty]
    private string? _detectedVersion;

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private int _exitCode;

    [ObservableProperty]
    private string? _fullOutput;
}

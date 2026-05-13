using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.ViewModels;

/// <summary>
/// One row in the Sessions tree. Represents either a session (with the main agent's
/// live state) or a subagent. Properties are flat strings/numbers so the XAML row
/// template can stay simple — the panel ViewModel is responsible for keeping them
/// in sync with the underlying SessionActivityState/AgentInstance.
/// </summary>
public partial class SessionTreeNode : ObservableObject
{
    /// <summary>
    /// Stable identity used to look up existing nodes when rebuilding the tree.
    /// For a session row this is the session ID; for a subagent it's the agent ID.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// True if this row represents a session (rendered as a top-level group).
    /// </summary>
    public bool IsSession { get; set; }

    /// <summary>
    /// Working directory of the session this row belongs to. Subagent rows inherit
    /// it from their parent session. Used to open or focus the corresponding tab
    /// from the Sessions panel (context menu / double-click).
    /// </summary>
    [ObservableProperty]
    private string? _workingDirectory;

    public bool HasWorkingDirectory => !string.IsNullOrEmpty(WorkingDirectory);

    partial void OnWorkingDirectoryChanged(string? value) => OnPropertyChanged(nameof(HasWorkingDirectory));

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _subtitle;

    /// <summary>
    /// Short description of what the agent is doing right now
    /// (e.g., "Edit: foo.cs", "Thinking...", "Idle").
    /// </summary>
    [ObservableProperty]
    private string _activity = "";

    /// <summary>
    /// Whether to show the animated spinner instead of a static state icon.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Static glyph shown when not busy (e.g., "✓" complete, "⚠" error, "·" idle).
    /// </summary>
    [ObservableProperty]
    private string _stateIcon = "·";

    [ObservableProperty]
    private int _usageTokens;

    [ObservableProperty]
    private int _maxTokens = 200_000;

    /// <summary>
    /// 0–100, bound to the ProgressBar's Value.
    /// </summary>
    [ObservableProperty]
    private double _usagePercent;

    /// <summary>
    /// Human-readable usage line (e.g., "45.2k / 200k · 23%").
    /// Null when no token data is available for this agent (common for
    /// Task-tool subagents whose per-message usage isn't bubbled up).
    /// </summary>
    [ObservableProperty]
    private string? _usageText;

    public ObservableCollection<SessionTreeNode> Children { get; } = new();
}

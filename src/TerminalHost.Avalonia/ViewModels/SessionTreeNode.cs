using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.ViewModels;

/// <summary>
/// One row in the Sessions tree. Represents either a session (with the main agent's
/// live state) or a subagent. Properties are flat strings/numbers so the AXAML row
/// template can stay simple — the panel ViewModel is responsible for keeping them
/// in sync with the underlying SessionActivityState/AgentInstance.
/// </summary>
public partial class SessionTreeNode : ObservableObject
{
    public string Id { get; set; } = "";

    public bool IsSession { get; set; }

    [ObservableProperty]
    private string? _workingDirectory;

    public bool HasWorkingDirectory => !string.IsNullOrEmpty(WorkingDirectory);

    partial void OnWorkingDirectoryChanged(string? value) => OnPropertyChanged(nameof(HasWorkingDirectory));

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string? _subtitle;

    [ObservableProperty]
    private string _activity = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _stateIcon = "·";

    [ObservableProperty]
    private int _usageTokens;

    [ObservableProperty]
    private int _maxTokens = 200_000;

    [ObservableProperty]
    private double _usagePercent;

    [ObservableProperty]
    private string? _usageText;

    public ObservableCollection<SessionTreeNode> Children { get; } = new();
}

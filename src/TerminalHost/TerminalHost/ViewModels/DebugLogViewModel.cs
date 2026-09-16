using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Debug Log panel (Ctrl+Shift+D replaced — use command palette).
/// Shows diagnostic messages from MCP, Memory, Ollama, and other subsystems.
/// </summary>
public partial class DebugLogViewModel : BasePanelViewModel, IPanelPlacement
{
    public PanelZone PreferredZone => PanelZone.Center;

    private readonly IDebugLogService _debugLog;
    private readonly Dispatcher _dispatcher;

    public override string PanelId => "debugLog";
    public override string PanelTitle => "Debug Log";
    public override string PanelIcon => "\uD83D\uDC1B"; // 🐛
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private int _totalCount;

    public DebugLogViewModel(IDebugLogService debugLog)
    {
        _debugLog = debugLog;
        _dispatcher = Dispatcher.CurrentDispatcher;
        DisplayState = PanelDisplayState.Panel;

        _debugLog.EntryAdded += OnEntryAdded;
    }

    public void Open()
    {
        RefreshEntries();
        RequestShow();
    }

    [RelayCommand]
    private void ClearLog()
    {
        _debugLog.Clear();
        Entries.Clear();
        TotalCount = 0;
    }

    [RelayCommand]
    private void Refresh() => RefreshEntries();

    partial void OnFilterTextChanged(string value) => RefreshEntries();
    partial void OnShowInfoChanged(bool value) => RefreshEntries();
    partial void OnShowWarningsChanged(bool value) => RefreshEntries();
    partial void OnShowErrorsChanged(bool value) => RefreshEntries();

    private void OnEntryAdded(DebugLogEntry entry)
    {
        _dispatcher.BeginInvoke(() =>
        {
            TotalCount = _debugLog.RecentEntries.Count;
            if (MatchesFilter(entry))
            {
                Entries.Insert(0, entry);
                // Keep UI list manageable
                while (Entries.Count > 500)
                    Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        var all = _debugLog.RecentEntries;
        TotalCount = all.Count;
        foreach (var entry in all)
        {
            if (MatchesFilter(entry))
                Entries.Add(entry);
        }
    }

    private bool MatchesFilter(DebugLogEntry entry)
    {
        if (entry.Level == DebugLogLevel.Info && !ShowInfo) return false;
        if (entry.Level == DebugLogLevel.Warning && !ShowWarnings) return false;
        if (entry.Level == DebugLogLevel.Error && !ShowErrors) return false;
        if (!string.IsNullOrEmpty(FilterText)
            && !entry.Source.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            && !entry.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}

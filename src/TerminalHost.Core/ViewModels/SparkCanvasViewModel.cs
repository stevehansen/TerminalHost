using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the Spark Canvas panel. Shared between WPF and Avalonia.
/// All business logic lives in <see cref="SparkCanvasOrchestrator"/>; this VM is
/// only the IPanelableViewModel adapter and a holder for the file-dialog event
/// (which is platform-specific so lives in the View).
/// </summary>
public sealed partial class SparkCanvasViewModel : BasePanelViewModel, IDisposable
{
    private readonly SparkCanvasOrchestrator _orchestrator;

    public SparkCanvasViewModel(SparkCanvasOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _orchestrator.StateChanged += (_, _) => OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Wires a platform-specific transport to the orchestrator.</summary>
    public void AttachTransport(ICanvasTransport transport) => _orchestrator.Attach(transport);

    /// <summary>Raised when the user wants to pick a JSONL file. View shows the dialog.</summary>
    public event EventHandler? RequestOpenJsonlFile;

    /// <summary>
    /// True if the open-jsonl command fired before the view subscribed. The Avalonia
    /// view checks this and re-triggers the dialog on attach.
    /// </summary>
    public bool HasPendingJsonlOpen { get; private set; }

    public override string PanelId => "sparkCanvas";
    public override string PanelTitle => "Spark";
    public override string PanelIcon => "✨"; // Sparkles
    public override PanelSizePreset SizePreset => PanelSizePreset.Full;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "📂",
            Tooltip = "Load JSONL transcript file",
            Command = OpenJsonlFileCommand
        },
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh session list",
            Command = RefreshSessionsCommand
        },
        new PanelHeaderCommand
        {
            Icon = "✖",
            Tooltip = "Close Spark Canvas",
            Command = CloseCommand
        }
    ];

    public override string? StatusText => _orchestrator.State switch
    {
        CanvasState.Single s => $"Session: {s.SessionId[..Math.Min(8, s.SessionId.Length)]}…",
        CanvasState.Multi m  => $"Multi: {m.SessionIds.Count} sessions",
        CanvasState.Replay r => $"Replay: {Path.GetFileName(r.FilePath)}",
        _                    => "No session selected"
    };

    /// <summary>Convenience for callers that want to open a specific session at construction time.</summary>
    public Task OpenSessionAsync(string sessionId) => _orchestrator.OpenSessionAsync(sessionId);

    /// <summary>Loads a JSONL transcript and pushes the parsed replay to the canvas.</summary>
    public Task LoadJsonlFileAsync(string filePath) => _orchestrator.OpenJsonlAsync(filePath);

    [RelayCommand]
    private void OpenJsonlFile()
    {
        if (RequestOpenJsonlFile != null)
        {
            HasPendingJsonlOpen = false;
            RequestOpenJsonlFile.Invoke(this, EventArgs.Empty);
        }
        else
        {
            HasPendingJsonlOpen = true;
        }
    }

    [RelayCommand]
    private Task RefreshSessions() => _orchestrator.RefreshSessionsAsync();

    public void Dispose() => _orchestrator.Dispose();
}

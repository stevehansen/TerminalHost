using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the voice command floating bar.
/// Manages the full voice flow: Idle → Listening → Processing → Preview/NoMatch → Executed.
/// </summary>
public partial class VoiceBarViewModel : ObservableObject
{
    private readonly ITimerService _timerService;
    private IAppTimer? _countdownTimer;
    private IAppTimer? _dismissTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(IsListening))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsNoMatch))]
    [NotifyPropertyChangedFor(nameof(IsExecuted))]
    [NotifyPropertyChangedFor(nameof(ShowCountdown))]
    [NotifyPropertyChangedFor(nameof(ShowSendToAi))]
    [NotifyPropertyChangedFor(nameof(ShowCancel))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    private VoiceFlowState _state = VoiceFlowState.Idle;

    [ObservableProperty]
    private string _transcript = "";

    [ObservableProperty]
    private string _matchedCommandName = "";

    [ObservableProperty]
    private string _matchedCommandShortcut = "";

    [ObservableProperty]
    private int _countdownSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountdownProgress))]
    private int _countdownRemaining;

    [ObservableProperty]
    private float _confidence;

    [ObservableProperty]
    private List<VoiceCommandMatch> _alternatives = [];

    /// <summary>
    /// The currently matched command entry (for execution).
    /// </summary>
    private VoiceCommandMatch? _currentMatch;

    /// <summary>
    /// Raised when the user chooses "Send to AI" for unmatched text.
    /// </summary>
    public event EventHandler<string>? SendToAiRequested;

    /// <summary>
    /// Raised when the bar wants to start voice listening.
    /// </summary>
    public event EventHandler? StartListeningRequested;

    /// <summary>
    /// Raised when the bar wants to stop voice listening.
    /// </summary>
    public event EventHandler? StopListeningRequested;

    // Computed properties
    public bool IsVisible => State != VoiceFlowState.Idle;
    public bool IsListening => State == VoiceFlowState.Listening;
    public bool IsPreview => State == VoiceFlowState.Preview;
    public bool IsNoMatch => State == VoiceFlowState.NoMatch;
    public bool IsExecuted => State == VoiceFlowState.Executed;
    public bool ShowCountdown => State == VoiceFlowState.Preview;
    public bool ShowSendToAi => State == VoiceFlowState.NoMatch;
    public bool ShowCancel => State is VoiceFlowState.Preview or VoiceFlowState.Listening;

    /// <summary>
    /// Countdown progress (1.0 = full, 0.0 = expired) for the shrinking bar animation.
    /// </summary>
    public double CountdownProgress => CountdownSeconds > 0
        ? (double)CountdownRemaining / CountdownSeconds
        : 0;

    public string StatusIcon => State switch
    {
        VoiceFlowState.Listening => "\U0001F3A4",   // 🎤
        VoiceFlowState.Processing => "\u23F3",       // ⏳
        VoiceFlowState.Preview => "\u2192",          // →
        VoiceFlowState.NoMatch => "\u2753",          // ❓
        VoiceFlowState.Executed => "\u2713",         // ✓
        _ => ""
    };

    public VoiceBarViewModel(ITimerService timerService)
    {
        _timerService = timerService;
    }

    /// <summary>
    /// Begin the voice flow — transition to Listening state and show the bar.
    /// </summary>
    [RelayCommand]
    public void StartListening()
    {
        Reset();
        State = VoiceFlowState.Listening;
        Transcript = "Listening...";
        StartListeningRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stop listening (user released key or toggled off).
    /// Transitions to Processing if we have no result yet.
    /// </summary>
    public void StopListening()
    {
        if (State == VoiceFlowState.Listening)
        {
            State = VoiceFlowState.Processing;
            Transcript = "Processing...";
            StopListeningRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called when speech recognition produces a result.
    /// Routes based on detected intent: command match, send-to-AI, confirm, or cancel.
    /// </summary>
    public void OnRecognitionResult(VoiceCommandResult result)
    {
        Transcript = result.Transcript;

        switch (result.Intent)
        {
            case VoiceIntent.SendToAi:
                // User explicitly said "send to claude: ..." or "... send to AI"
                var aiText = result.AiMessage ?? result.Transcript;
                StopCountdown();
                Reset();
                SendToAiRequested?.Invoke(this, aiText);
                return;

            case VoiceIntent.Confirm:
                // User said "yes" / "go" — execute immediately if we're in Preview
                if (State == VoiceFlowState.Preview)
                    ExecuteNow();
                return;

            case VoiceIntent.Cancel:
                // User said "no" / "cancel" — dismiss
                Cancel();
                return;

            case VoiceIntent.Command:
            default:
                break;
        }

        // Normal command matching flow
        Alternatives = result.Alternatives;

        if (result.IsMatch && result.BestMatch is not null)
        {
            _currentMatch = result.BestMatch;
            Confidence = result.BestMatch.Confidence;
            MatchedCommandName = result.BestMatch.Command.DisplayName;
            MatchedCommandShortcut = result.BestMatch.Command.Shortcut ?? "";
            CountdownSeconds = result.BestMatch.CountdownSeconds;
            CountdownRemaining = CountdownSeconds;
            State = VoiceFlowState.Preview;
            StartCountdown();
        }
        else if (result.BestMatch is not null && result.Alternatives.Count > 0)
        {
            // Partial match — show alternatives, no auto-execute
            _currentMatch = null;
            Confidence = result.BestMatch.Confidence;
            MatchedCommandName = "";
            State = VoiceFlowState.NoMatch;
        }
        else
        {
            // No match at all
            _currentMatch = null;
            Confidence = 0;
            MatchedCommandName = "";
            State = VoiceFlowState.NoMatch;
        }
    }

    /// <summary>
    /// User picked an alternative command from the list.
    /// </summary>
    [RelayCommand]
    private void SelectAlternative(VoiceCommandMatch match)
    {
        _currentMatch = match;
        Confidence = match.Confidence;
        MatchedCommandName = match.Command.DisplayName;
        MatchedCommandShortcut = match.Command.Shortcut ?? "";
        CountdownSeconds = match.CountdownSeconds;
        CountdownRemaining = CountdownSeconds;
        State = VoiceFlowState.Preview;
        StartCountdown();
    }

    /// <summary>
    /// Execute the matched command immediately (skip remaining countdown).
    /// </summary>
    [RelayCommand]
    private void ExecuteNow()
    {
        if (_currentMatch is null) return;
        StopCountdown();
        ExecuteCurrentMatch();
    }

    /// <summary>
    /// Cancel the current voice flow and dismiss the bar.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        StopCountdown();
        StopListeningRequested?.Invoke(this, EventArgs.Empty);
        Reset();
    }

    /// <summary>
    /// Send the transcript text to the AI terminal.
    /// </summary>
    [RelayCommand]
    private void SendToAi()
    {
        var text = Transcript;
        StopCountdown();
        Reset();
        if (!string.IsNullOrWhiteSpace(text) && text != "Listening..." && text != "Processing...")
        {
            SendToAiRequested?.Invoke(this, text);
        }
    }

    private void StartCountdown()
    {
        StopCountdown();
        _countdownTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(1), OnCountdownTick);
        _countdownTimer.Start();
    }

    private void OnCountdownTick()
    {
        CountdownRemaining--;
        OnPropertyChanged(nameof(CountdownProgress));

        if (CountdownRemaining <= 0)
        {
            StopCountdown();
            ExecuteCurrentMatch();
        }
    }

    private void ExecuteCurrentMatch()
    {
        if (_currentMatch is null) return;

        try
        {
            _currentMatch.Command.Execute();
        }
        catch
        {
            // Don't let command execution failures crash the voice flow
        }

        State = VoiceFlowState.Executed;
        MatchedCommandName = $"Executed: {_currentMatch.Command.DisplayName}";

        // Auto-dismiss after 1.5 seconds
        _dismissTimer = _timerService.CreateTimer(TimeSpan.FromMilliseconds(1500), () =>
        {
            _dismissTimer?.Stop();
            _dismissTimer?.Dispose();
            _dismissTimer = null;
            Reset();
        });
        _dismissTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
    }

    private void Reset()
    {
        StopCountdown();
        _dismissTimer?.Stop();
        _dismissTimer?.Dispose();
        _dismissTimer = null;
        _currentMatch = null;
        State = VoiceFlowState.Idle;
        Transcript = "";
        MatchedCommandName = "";
        MatchedCommandShortcut = "";
        CountdownSeconds = 0;
        CountdownRemaining = 0;
        Confidence = 0;
        Alternatives = [];
    }
}

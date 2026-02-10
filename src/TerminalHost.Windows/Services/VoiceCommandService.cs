using System.Speech.Recognition;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of IVoiceCommandService using System.Speech.Recognition.
/// Uses constrained grammar mode for high accuracy with known command vocabulary.
/// The speech engine is lazy-loaded and only initialized when voice commands are enabled.
/// </summary>
public sealed class VoiceCommandService : IVoiceCommandService, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IDispatcherService _dispatcherService;
    private readonly VoiceCommandMatcher _matcher = new();

    private SpeechRecognitionEngine? _engine;
    private bool _isAvailable;
    private bool _isListening;
    private bool _disposed;
    private IReadOnlyList<VoiceCommandEntry> _currentCommands = [];

    public bool IsAvailable => _isAvailable;
    public bool IsListening => _isListening;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;
    public event EventHandler<VoiceCommandErrorEventArgs>? Error;
    public event EventHandler? ListeningStateChanged;

    public VoiceCommandService(IConfigurationService configService, IDispatcherService dispatcherService)
    {
        _configService = configService;
        _dispatcherService = dispatcherService;

        // Probe availability without fully initializing
        _isAvailable = CheckAvailability();
    }

    /// <summary>
    /// Check if System.Speech is available on this machine.
    /// </summary>
    private static bool CheckAvailability()
    {
        try
        {
            using var testEngine = new SpeechRecognitionEngine();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initialize the speech recognition engine (lazy, first use only).
    /// </summary>
    private bool EnsureEngine()
    {
        if (_engine is not null) return true;
        if (!_isAvailable) return false;

        try
        {
            _engine = new SpeechRecognitionEngine();
            _engine.SetInputToDefaultAudioDevice();

            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.SpeechRecognitionRejected += OnSpeechRejected;
            _engine.RecognizeCompleted += OnRecognizeCompleted;

            // Build grammar from current commands
            RebuildGrammar();

            return true;
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            RaiseError($"Failed to initialize speech engine: {ex.Message}", isFatal: true);
            return false;
        }
    }

    public void UpdateGrammar(IReadOnlyList<VoiceCommandEntry> commands)
    {
        _currentCommands = commands;
        _matcher.SetCommands(commands);

        // If engine is already running, rebuild grammar
        if (_engine is not null)
        {
            RebuildGrammar();
        }
    }

    /// <summary>
    /// Build a constrained grammar from all registered voice commands + meta-commands.
    /// Uses GrammarBuilder with Choices for high accuracy recognition.
    /// Falls back to DictationGrammar if no commands are registered.
    /// </summary>
    private void RebuildGrammar()
    {
        if (_engine is null) return;

        var wasListening = _isListening;
        if (wasListening)
        {
            try { _engine.RecognizeAsyncCancel(); } catch { }
        }

        _engine.UnloadAllGrammars();

        var phrases = new List<string>();

        // Add all command phrases
        foreach (var cmd in _currentCommands)
        {
            foreach (var phrase in cmd.AllPhrases)
            {
                if (!string.IsNullOrWhiteSpace(phrase))
                    phrases.Add(phrase.ToLowerInvariant());
            }
        }

        // Add meta-command keywords (confirm, cancel, send-to-AI prefixes)
        phrases.AddRange(["yes", "yep", "yeah", "confirm", "go", "do it", "execute", "proceed"]);
        phrases.AddRange(["no", "nope", "cancel", "stop", "nevermind", "abort", "dismiss"]);

        if (phrases.Count > 0)
        {
            try
            {
                // Constrained grammar: only recognize these phrases (high accuracy)
                var choices = new Choices(phrases.ToArray());
                var builder = new GrammarBuilder(choices);
                var grammar = new Grammar(builder) { Name = "VoiceCommands" };
                _engine.LoadGrammar(grammar);
            }
            catch
            {
                // Fallback to dictation grammar if constrained fails
            }
        }

        // Also add a dictation grammar with lower priority for free-form speech
        // (captures "send to claude: fix the bug" and similar)
        try
        {
            var dictation = new DictationGrammar { Name = "Dictation" };
            dictation.Weight = 0.3f; // Lower weight than constrained grammar
            _engine.LoadGrammar(dictation);
        }
        catch
        {
            // Dictation may not be available
        }

        if (wasListening)
        {
            try { _engine.RecognizeAsync(RecognizeMode.Single); } catch { }
        }
    }

    public void StartListening()
    {
        if (_isListening) return;
        if (!EnsureEngine()) return;

        try
        {
            _engine!.RecognizeAsync(RecognizeMode.Single);
            _isListening = true;
            _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to start listening: {ex.Message}", isFatal: false);
        }
    }

    public void StopListening()
    {
        if (!_isListening || _engine is null) return;

        try
        {
            _engine.RecognizeAsyncCancel();
        }
        catch
        {
            // Ignore stop errors
        }

        _isListening = false;
        _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        _isListening = false;

        var transcript = e.Result.Text;
        var engineConfidence = e.Result.Confidence;

        // Run through our matcher (handles intents + fuzzy matching)
        var settings = _configService.Load().Settings.Voice;
        var result = _matcher.Match(transcript, settings.ConfidenceThreshold);

        // Boost confidence for constrained grammar matches (the engine already validated it)
        if (result.BestMatch is not null && result.Intent == VoiceIntent.Command)
        {
            // Blend engine confidence with our matcher confidence
            var blended = Math.Max(result.BestMatch.Confidence, engineConfidence);
            result = new VoiceCommandResult
            {
                Transcript = result.Transcript,
                Intent = result.Intent,
                BestMatch = result.BestMatch with { Confidence = blended },
                Alternatives = result.Alternatives,
                ConfidenceThreshold = result.ConfidenceThreshold
            };
        }

        _dispatcherService.BeginInvoke(() =>
        {
            CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs { Result = result });
            ListeningStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        _isListening = false;

        // Best-effort transcript from rejected speech
        var transcript = e.Result?.Text ?? "";

        var result = new VoiceCommandResult
        {
            Transcript = string.IsNullOrWhiteSpace(transcript) ? "(unrecognized)" : transcript,
            ConfidenceThreshold = _configService.Load().Settings.Voice.ConfidenceThreshold
        };

        _dispatcherService.BeginInvoke(() =>
        {
            CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs { Result = result });
            ListeningStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (_isListening)
        {
            _isListening = false;
            _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private void RaiseError(string message, bool isFatal)
    {
        _dispatcherService.BeginInvoke(() =>
        {
            Error?.Invoke(this, new VoiceCommandErrorEventArgs { Message = message, IsFatal = isFatal });
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isListening)
        {
            try { _engine?.RecognizeAsyncCancel(); } catch { }
        }

        _engine?.Dispose();
        _engine = null;
    }
}

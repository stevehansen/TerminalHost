using System.IO;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.LibraryLoader;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Windows.Services;

/// <summary>
/// IVoiceCommandService implementation using OpenAI Whisper via whisper.cpp (Whisper.net).
/// Provides open-vocabulary transcription with multi-language support, running fully offline.
/// Audio is captured via NAudio at 16kHz/16-bit/mono (Whisper's required format),
/// then processed in one shot after listening stops.
/// </summary>
public sealed class WhisperVoiceCommandService : IVoiceCommandService, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IDispatcherService _dispatcherService;
    private readonly WhisperModelManager _modelManager;
    private readonly VoiceCommandMatcher _matcher = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private BinaryWriter? _audioWriter;

    private bool _isAvailable = true; // Assume available until proven otherwise
    private bool _isListening;
    private bool _disposed;
    private bool _initializing;
    private bool _hasSpeechStarted;
    private int _silenceSampleCount;
    private IReadOnlyList<VoiceCommandEntry> _currentCommands = [];

    // Silence detection parameters
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const float SilenceRmsThreshold = 500f;  // RMS below this = silence
    private const int SilenceTimeoutMs = 2000;       // Auto-stop after 2s silence following speech
    private const int SilenceSamplesTimeout = SampleRate * SilenceTimeoutMs / 1000;

    public bool IsAvailable => _isAvailable;
    public bool IsListening => _isListening;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;
    public event EventHandler<VoiceCommandErrorEventArgs>? Error;
    public event EventHandler? ListeningStateChanged;

    public WhisperVoiceCommandService(
        IConfigurationService configService,
        IDispatcherService dispatcherService,
        WhisperModelManager modelManager)
    {
        _configService = configService;
        _dispatcherService = dispatcherService;
        _modelManager = modelManager;
    }

    public void UpdateGrammar(IReadOnlyList<VoiceCommandEntry> commands)
    {
        _currentCommands = commands;
        _matcher.SetCommands(commands);
        // No grammar rebuild needed — Whisper is open vocabulary
    }

    public async void StartListening()
    {
        if (_isListening || _initializing) return;

        // Ensure model is downloaded and processor is ready
        if (_processor is null)
        {
            _initializing = true;
            try
            {
                var success = await InitializeProcessorAsync();
                if (!success)
                {
                    _initializing = false;
                    return;
                }
            }
            finally
            {
                _initializing = false;
            }
        }

        try
        {
            // Initialize audio capture
            _audioBuffer = new MemoryStream();
            _audioWriter = new BinaryWriter(_audioBuffer);
            _hasSpeechStarted = false;
            _silenceSampleCount = 0;

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += OnAudioDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();

            _isListening = true;
            _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            CleanupAudioCapture();
            _isAvailable = false;
            RaiseError($"Failed to start microphone: {ex.Message}", isFatal: true);
        }
    }

    public void StopListening()
    {
        if (!_isListening) return;

        _isListening = false;

        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // Ignore stop errors, processing will happen in OnRecordingStopped
        }

        _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isListening || _audioWriter is null) return;

        // Write raw PCM data to buffer
        _audioWriter.Write(e.Buffer, 0, e.BytesRecorded);

        // Simple RMS-based voice activity detection
        var rms = CalculateRms(e.Buffer, e.BytesRecorded);

        if (rms > SilenceRmsThreshold)
        {
            _hasSpeechStarted = true;
            _silenceSampleCount = 0;
        }
        else if (_hasSpeechStarted)
        {
            _silenceSampleCount += e.BytesRecorded / (BitsPerSample / 8);
            if (_silenceSampleCount >= SilenceSamplesTimeout)
            {
                // Auto-stop after sustained silence following speech
                StopListening();
            }
        }
    }

    private async void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_processor is null || _audioBuffer is null)
        {
            CleanupAudioCapture();
            return;
        }

        try
        {
            // Convert raw PCM to float samples for Whisper
            var pcmData = _audioBuffer.ToArray();
            var floatSamples = ConvertPcm16ToFloat(pcmData);

            if (floatSamples.Length < SampleRate / 2) // Less than 0.5s of audio
            {
                // Too short, treat as empty
                var emptyResult = new VoiceCommandResult
                {
                    Transcript = "(too short)",
                    ConfidenceThreshold = _configService.Load().Settings.Voice.ConfidenceThreshold
                };
                _dispatcherService.BeginInvoke(() =>
                {
                    CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs { Result = emptyResult });
                });
                CleanupAudioCapture();
                return;
            }

            // Run Whisper transcription
            var transcript = await TranscribeAudioAsync(floatSamples);
            CleanupAudioCapture();

            if (string.IsNullOrWhiteSpace(transcript))
            {
                var noResult = new VoiceCommandResult
                {
                    Transcript = "(no speech detected)",
                    ConfidenceThreshold = _configService.Load().Settings.Voice.ConfidenceThreshold
                };
                _dispatcherService.BeginInvoke(() =>
                {
                    CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs { Result = noResult });
                });
                return;
            }

            // Match against commands using the shared matcher
            var settings = _configService.Load().Settings.Voice;
            var result = _matcher.Match(transcript.Trim(), settings.ConfidenceThreshold);

            _dispatcherService.BeginInvoke(() =>
            {
                CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs { Result = result });
            });
        }
        catch (Exception ex)
        {
            CleanupAudioCapture();
            RaiseError($"Transcription failed: {ex.Message}", isFatal: false);
        }
    }

    private async Task<string> TranscribeAudioAsync(float[] samples)
    {
        if (_processor is null) return "";

        var segments = new List<string>();
        await foreach (var segment in _processor.ProcessAsync(samples))
        {
            var text = segment.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                segments.Add(text);
        }

        return string.Join(" ", segments);
    }

    private async Task<bool> InitializeProcessorAsync()
    {
        var config = _configService.Load();
        var modelSize = config.Settings.Voice.WhisperModelSize;
        var language = config.Settings.Voice.WhisperLanguage;

        var modelPath = await _modelManager.EnsureModelAsync(modelSize);
        if (modelPath is null)
        {
            _isAvailable = false;
            RaiseError("Whisper model download failed. Check your internet connection and try again.", isFatal: true);
            return false;
        }

        try
        {
            // Ensure the native library loader can find whisper DLLs.
            // In single-file publish, the runtimes/ folder may be next to the exe
            // or in the NuGet cache (debug builds). Probe known locations.
            EnsureNativeLibraryPath();

            _factory = WhisperFactory.FromPath(modelPath);
            var builder = _factory.CreateBuilder()
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2));

            if (!string.IsNullOrEmpty(language) && !string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
            {
                builder.WithLanguage(language);
            }
            else
            {
                builder.WithLanguage("auto");
            }

            _processor = builder.Build();
            return true;
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            RaiseError($"Failed to initialize Whisper: {ex.Message}", isFatal: true);
            return false;
        }
    }

    /// <summary>
    /// Probe for the whisper native DLLs in known locations and set RuntimeOptions.LibraryPath
    /// if found. This is needed for single-file publish where the runtimes/ folder is alongside
    /// the exe but Whisper.net's default probe paths may not find it.
    /// </summary>
    private static void EnsureNativeLibraryPath()
    {
        // Already set by a previous call
        if (!string.IsNullOrEmpty(RuntimeOptions.LibraryPath))
            return;

        // Candidate directories to check for runtimes/win-x64/whisper.dll
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
        };

        foreach (var baseDir in candidates)
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            var runtimeDir = Path.Combine(baseDir, "runtimes", "win-x64");
            if (File.Exists(Path.Combine(runtimeDir, "whisper.dll")))
            {
                RuntimeOptions.LibraryPath = runtimeDir;
                return;
            }
        }
    }

    private void CleanupAudioCapture()
    {
        _audioWriter?.Dispose();
        _audioWriter = null;
        _audioBuffer?.Dispose();
        _audioBuffer = null;

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnAudioDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private static float CalculateRms(byte[] buffer, int bytesRecorded)
    {
        double sumSquares = 0;
        var sampleCount = bytesRecorded / 2; // 16-bit = 2 bytes per sample
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += sample * sample;
        }
        return (float)Math.Sqrt(sumSquares / Math.Max(sampleCount, 1));
    }

    private static float[] ConvertPcm16ToFloat(byte[] pcmData)
    {
        var sampleCount = pcmData.Length / 2;
        var floatSamples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            var sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
            floatSamples[i] = sample / 32768f;
        }
        return floatSamples;
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
            try { _waveIn?.StopRecording(); } catch { }
        }

        CleanupAudioCapture();
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
    }
}

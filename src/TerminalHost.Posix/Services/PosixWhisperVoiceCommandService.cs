using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.LibraryLoader;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Posix.Services;

/// <summary>
/// IVoiceCommandService implementation using OpenAI Whisper via whisper.cpp (Whisper.net).
/// Provides open-vocabulary transcription with multi-language support, running fully offline.
/// Audio is captured via IAudioCaptureService at 16kHz/16-bit/mono (Whisper's required format),
/// then processed in one shot after listening stops.
/// </summary>
public sealed class PosixWhisperVoiceCommandService : IVoiceCommandService, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IDispatcherService _dispatcherService;
    private readonly WhisperModelManager _modelManager;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly VoiceCommandMatcher _matcher = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private MemoryStream? _audioBuffer;
    private BinaryWriter? _audioWriter;

    private bool _isAvailable;
    private bool _isListening;
    private bool _disposed;
    private bool _initializing;
    private IReadOnlyList<VoiceCommandEntry> _currentCommands = [];

    private const int SampleRate = 16000;

    public bool IsAvailable => _isAvailable;
    public bool IsListening => _isListening;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;
    public event EventHandler<VoiceCommandErrorEventArgs>? Error;
    public event EventHandler? ListeningStateChanged;

    public PosixWhisperVoiceCommandService(
        IConfigurationService configService,
        IDispatcherService dispatcherService,
        WhisperModelManager modelManager,
        IAudioCaptureService audioCaptureService)
    {
        _configService = configService;
        _dispatcherService = dispatcherService;
        _modelManager = modelManager;
        _audioCaptureService = audioCaptureService;
        _isAvailable = audioCaptureService.IsAvailable;

        // Subscribe to audio capture events
        _audioCaptureService.DataAvailable += OnAudioDataAvailable;
        _audioCaptureService.RecordingStopped += OnRecordingStopped;
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
            // Initialize audio buffer
            _audioBuffer = new MemoryStream();
            _audioWriter = new BinaryWriter(_audioBuffer);

            // Start audio capture
            _audioCaptureService.StartRecording();

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
            var audioData = _audioCaptureService.StopRecording();
            ProcessRecordedAudio(audioData);
        }
        catch (Exception ex)
        {
            CleanupAudioCapture();
            RaiseError($"Error stopping recording: {ex.Message}", isFatal: false);
        }

        _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
    {
        if (!_isListening || _audioWriter is null) return;

        // Write raw PCM data to buffer
        _audioWriter.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, EventArgs e)
    {
        if (_isListening)
        {
            // Auto-stop triggered by silence detection
            _isListening = false;
            var audioData = _audioCaptureService.StopRecording();
            ProcessRecordedAudio(audioData);
            _dispatcherService.BeginInvoke(() => ListeningStateChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private async void ProcessRecordedAudio(byte[] pcmData)
    {
        if (_processor is null)
        {
            CleanupAudioCapture();
            return;
        }

        try
        {
            // Also include any data we captured via DataAvailable
            byte[] combinedData;
            lock (this)
            {
                if (_audioBuffer != null && _audioBuffer.Length > 0)
                {
                    var bufferedData = _audioBuffer.ToArray();
                    combinedData = pcmData.Length > 0 ? bufferedData : pcmData;
                    if (pcmData.Length > 0 && bufferedData.Length > pcmData.Length)
                    {
                        combinedData = bufferedData;
                    }
                }
                else
                {
                    combinedData = pcmData;
                }
            }

            // Convert raw PCM to float samples for Whisper
            var floatSamples = ConvertPcm16ToFloat(combinedData);

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
            // Ensure the native library loader can find whisper libraries.
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
    /// Probe for the whisper native libraries in known locations and set RuntimeOptions.LibraryPath
    /// if found. This handles Linux and macOS library paths.
    /// </summary>
    private static void EnsureNativeLibraryPath()
    {
        // Already set by a previous call
        if (!string.IsNullOrEmpty(RuntimeOptions.LibraryPath))
            return;

        var rid = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
            : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64");

        var libName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "libwhisper.dylib"
            : "libwhisper.so";

        // Candidate directories to check for runtimes/{rid}/libwhisper.{so|dylib}
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
        };

        foreach (var baseDir in candidates)
        {
            if (string.IsNullOrEmpty(baseDir)) continue;

            // Check runtimes/{rid}/native/
            var runtimeDir = Path.Combine(baseDir, "runtimes", rid, "native");
            if (File.Exists(Path.Combine(runtimeDir, libName)))
            {
                RuntimeOptions.LibraryPath = runtimeDir;
                return;
            }

            // Check runtimes/{rid}/
            runtimeDir = Path.Combine(baseDir, "runtimes", rid);
            if (File.Exists(Path.Combine(runtimeDir, libName)))
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

        _audioCaptureService.DataAvailable -= OnAudioDataAvailable;
        _audioCaptureService.RecordingStopped -= OnRecordingStopped;

        if (_isListening)
        {
            try { _audioCaptureService.StopRecording(); } catch { }
        }

        CleanupAudioCapture();
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
    }
}

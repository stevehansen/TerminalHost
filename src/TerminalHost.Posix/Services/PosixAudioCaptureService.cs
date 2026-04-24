using System.Runtime.InteropServices;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// Cross-platform audio capture using OpenAL.
/// Captures microphone audio at 16kHz, 16-bit, mono (Whisper format).
/// </summary>
public sealed class PosixAudioCaptureService : IAudioCaptureService, IDisposable
{
    private const int SampleRate = 16000;
    private const int BufferSize = SampleRate; // 1 second of audio per callback
    private const int Format = OpenAL.AL_FORMAT_MONO16;

    private nint _captureDevice;
    private readonly List<byte> _recordedData = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _disposed;

    // Voice activity detection (same parameters as Windows version)
    private const float SilenceRmsThreshold = 500f;
    private const int SilenceTimeoutMs = 2000;
    private DateTime _lastSpeechTime;
    private bool _hasDetectedSpeech;

    public bool IsAvailable { get; private set; }
    public bool IsRecording { get; private set; }

    public event EventHandler<AudioDataEventArgs>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public PosixAudioCaptureService()
    {
        IsAvailable = OpenAL.IsAvailable;
    }

    public void StartRecording()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PosixAudioCaptureService));
        if (!IsAvailable) return;
        if (IsRecording) return;

        lock (_lock)
        {
            _recordedData.Clear();
        }

        // Open the default capture device
        _captureDevice = OpenAL.alcCaptureOpenDevice(
            null, // default device
            SampleRate,
            Format,
            BufferSize);

        if (_captureDevice == nint.Zero)
        {
            IsAvailable = false;
            return;
        }

        _hasDetectedSpeech = false;
        _lastSpeechTime = DateTime.UtcNow;

        // Start capturing
        OpenAL.alcCaptureStart(_captureDevice);
        IsRecording = true;

        // Start background task to poll for samples
        _captureCts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token));
    }

    public byte[] StopRecording()
    {
        if (!IsRecording)
            return Array.Empty<byte>();

        // Signal the capture loop to stop
        _captureCts?.Cancel();
        try
        {
            _captureTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore cancellation exceptions
        }

        // Stop and close the capture device
        if (_captureDevice != nint.Zero)
        {
            OpenAL.alcCaptureStop(_captureDevice);

            // Capture any remaining samples
            CaptureAvailableSamples();

            OpenAL.alcCaptureCloseDevice(_captureDevice);
            _captureDevice = nint.Zero;
        }

        IsRecording = false;
        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;

        lock (_lock)
        {
            return _recordedData.ToArray();
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var pollIntervalMs = 50; // Poll every 50ms
        while (!ct.IsCancellationRequested && IsRecording)
        {
            try
            {
                Thread.Sleep(pollIntervalMs);
                CaptureAvailableSamples();

                // Check for silence timeout (auto-stop after prolonged silence)
                if (_hasDetectedSpeech)
                {
                    var silenceDuration = DateTime.UtcNow - _lastSpeechTime;
                    if (silenceDuration.TotalMilliseconds > SilenceTimeoutMs)
                    {
                        // Auto-stop due to silence
                        RecordingStopped?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore errors in capture loop
            }
        }
    }

    private void CaptureAvailableSamples()
    {
        if (_captureDevice == nint.Zero) return;

        // Query how many samples are available
        OpenAL.alcGetIntegerv(_captureDevice, OpenAL.ALC_CAPTURE_SAMPLES, 1, out int samplesAvailable);
        if (samplesAvailable <= 0) return;

        // Allocate buffer for samples (16-bit mono = 2 bytes per sample)
        var byteCount = samplesAvailable * 2;
        var buffer = new byte[byteCount];

        // Capture the samples
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            OpenAL.alcCaptureSamples(_captureDevice, handle.AddrOfPinnedObject(), samplesAvailable);
        }
        finally
        {
            handle.Free();
        }

        // Calculate RMS for voice activity detection
        var rms = CalculateRms(buffer);
        if (rms > SilenceRmsThreshold)
        {
            _hasDetectedSpeech = true;
            _lastSpeechTime = DateTime.UtcNow;
        }

        // Store the data
        lock (_lock)
        {
            _recordedData.AddRange(buffer);
        }

        // Notify listeners
        DataAvailable?.Invoke(this, new AudioDataEventArgs
        {
            Buffer = buffer,
            BytesRecorded = byteCount
        });
    }

    private static float CalculateRms(byte[] buffer)
    {
        if (buffer.Length < 2) return 0;

        double sumSquares = 0;
        var sampleCount = buffer.Length / 2;

        for (int i = 0; i < buffer.Length - 1; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += sample * sample;
        }

        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsRecording)
        {
            StopRecording();
        }
    }

    /// <summary>
    /// OpenAL P/Invoke bindings for audio capture.
    /// </summary>
    private static class OpenAL
    {
        public const int AL_FORMAT_MONO16 = 0x1101;
        public const int ALC_CAPTURE_SAMPLES = 0x312;

        private const string LibNameMacOS = "/System/Library/Frameworks/OpenAL.framework/OpenAL";
        private const string LibNameLinux = "libopenal.so.1";

        public static bool IsAvailable { get; }
        private static readonly bool IsMacOS;
        private static readonly bool IsLinux;

        static OpenAL()
        {
            IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            // Test if OpenAL is available
            try
            {
                var device = alcCaptureOpenDevice(null, 16000, AL_FORMAT_MONO16, 1024);
                if (device != nint.Zero)
                {
                    alcCaptureCloseDevice(device);
                    IsAvailable = true;
                }
            }
            catch
            {
                IsAvailable = false;
            }
        }

        // Cross-platform P/Invoke wrappers
        public static nint alcCaptureOpenDevice(string? deviceName, int sampleRate, int format, int bufferSize)
        {
            if (IsMacOS)
                return alcCaptureOpenDevice_macOS(deviceName, sampleRate, format, bufferSize);
            if (IsLinux)
                return alcCaptureOpenDevice_Linux(deviceName, sampleRate, format, bufferSize);
            return nint.Zero;
        }

        public static void alcCaptureStart(nint device)
        {
            if (IsMacOS)
                alcCaptureStart_macOS(device);
            else if (IsLinux)
                alcCaptureStart_Linux(device);
        }

        public static void alcCaptureStop(nint device)
        {
            if (IsMacOS)
                alcCaptureStop_macOS(device);
            else if (IsLinux)
                alcCaptureStop_Linux(device);
        }

        public static bool alcCaptureCloseDevice(nint device)
        {
            if (IsMacOS)
                return alcCaptureCloseDevice_macOS(device);
            if (IsLinux)
                return alcCaptureCloseDevice_Linux(device);
            return false;
        }

        public static void alcCaptureSamples(nint device, nint buffer, int samples)
        {
            if (IsMacOS)
                alcCaptureSamples_macOS(device, buffer, samples);
            else if (IsLinux)
                alcCaptureSamples_Linux(device, buffer, samples);
        }

        public static void alcGetIntegerv(nint device, int param, int size, out int value)
        {
            value = 0;
            if (IsMacOS)
                alcGetIntegerv_macOS(device, param, size, out value);
            else if (IsLinux)
                alcGetIntegerv_Linux(device, param, size, out value);
        }

        // macOS P/Invoke
        [DllImport(LibNameMacOS, EntryPoint = "alcCaptureOpenDevice")]
        private static extern nint alcCaptureOpenDevice_macOS(
            [MarshalAs(UnmanagedType.LPStr)] string? deviceName,
            int sampleRate, int format, int bufferSize);

        [DllImport(LibNameMacOS, EntryPoint = "alcCaptureStart")]
        private static extern void alcCaptureStart_macOS(nint device);

        [DllImport(LibNameMacOS, EntryPoint = "alcCaptureStop")]
        private static extern void alcCaptureStop_macOS(nint device);

        [DllImport(LibNameMacOS, EntryPoint = "alcCaptureCloseDevice")]
        private static extern bool alcCaptureCloseDevice_macOS(nint device);

        [DllImport(LibNameMacOS, EntryPoint = "alcCaptureSamples")]
        private static extern void alcCaptureSamples_macOS(nint device, nint buffer, int samples);

        [DllImport(LibNameMacOS, EntryPoint = "alcGetIntegerv")]
        private static extern void alcGetIntegerv_macOS(nint device, int param, int size, out int value);

        // Linux P/Invoke
        [DllImport(LibNameLinux, EntryPoint = "alcCaptureOpenDevice")]
        private static extern nint alcCaptureOpenDevice_Linux(
            [MarshalAs(UnmanagedType.LPStr)] string? deviceName,
            int sampleRate, int format, int bufferSize);

        [DllImport(LibNameLinux, EntryPoint = "alcCaptureStart")]
        private static extern void alcCaptureStart_Linux(nint device);

        [DllImport(LibNameLinux, EntryPoint = "alcCaptureStop")]
        private static extern void alcCaptureStop_Linux(nint device);

        [DllImport(LibNameLinux, EntryPoint = "alcCaptureCloseDevice")]
        private static extern bool alcCaptureCloseDevice_Linux(nint device);

        [DllImport(LibNameLinux, EntryPoint = "alcCaptureSamples")]
        private static extern void alcCaptureSamples_Linux(nint device, nint buffer, int samples);

        [DllImport(LibNameLinux, EntryPoint = "alcGetIntegerv")]
        private static extern void alcGetIntegerv_Linux(nint device, int param, int size, out int value);
    }
}

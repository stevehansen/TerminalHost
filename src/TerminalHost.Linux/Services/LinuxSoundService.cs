using System.Runtime.InteropServices;
using TerminalHost.Core.Interfaces;
using TerminalHost.Posix.Services;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Sound notification service for Linux.
/// Uses libcanberra for system/themed sounds via P/Invoke, with CLI player fallback for custom files.
/// </summary>
public sealed class LinuxSoundService : PosixSoundServiceBase
{
    private static readonly Dictionary<string, string> SoundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["asterisk"] = "dialog-information",
        ["beep"] = "bell",
        ["exclamation"] = "dialog-warning",
        ["hand"] = "dialog-error",
        ["question"] = "dialog-question",
    };

    private readonly nint _caContext;
    private readonly string? _filePlayer;
    private int _playId;

    public LinuxSoundService(IConfigurationService configurationService)
        : base(configurationService)
    {
        // Try to initialize libcanberra context
        try
        {
            if (Canberra.IsAvailable)
            {
                // ca_context_create returns 0 on success, negative on failure
                var result = Canberra.ca_context_create(out _caContext);
                if (result != 0)
                    _caContext = nint.Zero;
            }
        }
        catch
        {
            _caContext = nint.Zero;
        }

        // Detect CLI player for custom file playback
        _filePlayer = DetectFilePlayer();
    }

    protected override void PlaySystemSound(string soundName)
    {
        if (!SoundMap.TryGetValue(soundName, out var eventId))
            return;

        if (_caContext != nint.Zero)
        {
            var id = Interlocked.Increment(ref _playId);
            // ca_context_play returns 0 on success, negative error code on failure
            var result = Canberra.ca_context_play(_caContext, (uint)id,
                Canberra.CA_PROP_EVENT_ID, eventId,
                Canberra.CA_PROP_CANBERRA_CACHE_CONTROL, "permanent",
                null);
            if (result == 0)
                return; // Success - no need to fall back to CLI
        }

        // Fallback: try CLI canberra-gtk-play
        if (CanRun("canberra-gtk-play"))
        {
            StartDetached("canberra-gtk-play", "-i", eventId);
        }
    }

    protected override void PlayFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // libcanberra can also play files directly via the media.filename property
        if (_caContext != nint.Zero)
        {
            var id = Interlocked.Increment(ref _playId);
            // ca_context_play returns 0 on success, negative error code on failure
            var result = Canberra.ca_context_play(_caContext, (uint)id,
                Canberra.CA_PROP_MEDIA_FILENAME, filePath,
                null);
            if (result == 0)
                return; // Success - no need to fall back to CLI
        }

        // Fallback to CLI player
        if (_filePlayer == null) return;

        if (_filePlayer == "aplay")
            StartDetached("aplay", "-q", filePath);
        else
            StartDetached(_filePlayer, filePath);
    }

    private static string? DetectFilePlayer()
    {
        if (CanRun("pw-play")) return "pw-play";
        if (CanRun("paplay")) return "paplay";
        if (CanRun("aplay")) return "aplay";
        return null;
    }

    /// <summary>
    /// P/Invoke bindings for libcanberra (freedesktop event sound API).
    /// </summary>
    private static class Canberra
    {
        private const string LibName = "libcanberra.so.0";

        // Property key constants
        public const string CA_PROP_EVENT_ID = "event.id";
        public const string CA_PROP_MEDIA_FILENAME = "media.filename";
        public const string CA_PROP_CANBERRA_CACHE_CONTROL = "canberra.cache-control";

        public static readonly bool IsAvailable;

        static Canberra()
        {
            IsAvailable = NativeLibrary.TryLoad(LibName, out _);
        }

        [DllImport(LibName)]
        public static extern int ca_context_create(out nint context);

        [DllImport(LibName)]
        public static extern int ca_context_destroy(nint context);

        // ca_context_play is variadic: (context, id, key1, val1, key2, val2, ..., NULL)
        // We expose overloads for the argument counts we actually use.

        [DllImport(LibName)]
        public static extern int ca_context_play(nint context, uint id,
            string key1, string val1,
            string key2, string val2,
            string? sentinel);

        [DllImport(LibName)]
        public static extern int ca_context_play(nint context, uint id,
            string key1, string val1,
            string? sentinel);
    }
}

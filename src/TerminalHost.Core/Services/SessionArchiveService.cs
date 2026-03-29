using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Persists session summaries to disk so devcontainer sessions survive app restart.
/// Stored in %APPDATA%/TerminalHost/session-archive/ (one JSON file per session).
/// </summary>
public class SessionArchiveService : ISessionArchiveService
{
    private readonly IFileSystem _fileSystem;
    private readonly string _archiveDir;

    public SessionArchiveService(IFileSystem fileSystem, string? userDataDir = null)
    {
        _fileSystem = fileSystem;
        var baseDir = userDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TerminalHost");
        _archiveDir = Path.Combine(baseDir, "session-archive");
    }

    public void ArchiveSession(SessionActivityState state)
    {
        try
        {
            if (!_fileSystem.DirectoryExists(_archiveDir))
                _fileSystem.CreateDirectory(_archiveDir);

            var entry = SessionArchiveEntry.FromState(state);
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(_archiveDir, $"{state.SessionId}.json");
            _fileSystem.WriteAllText(filePath, json);
        }
        catch
        {
            // Archive is best-effort — don't crash on failure
        }
    }

    public IReadOnlyList<SessionArchiveEntry> GetArchivedSessions(TimeSpan? maxAge = null)
    {
        var results = new List<SessionArchiveEntry>();
        if (!_fileSystem.DirectoryExists(_archiveDir))
            return results;

        try
        {
            var files = _fileSystem.GetFiles(_archiveDir, "*.json", SearchOption.TopDirectoryOnly);
            var cutoff = maxAge.HasValue ? DateTime.UtcNow - maxAge.Value : DateTime.MinValue;

            foreach (var file in files)
            {
                try
                {
                    var json = _fileSystem.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<SessionArchiveEntry>(json);
                    if (entry != null && entry.ArchivedAt >= cutoff)
                        results.Add(entry);
                }
                catch
                {
                    // Skip malformed entries
                }
            }
        }
        catch
        {
            // Directory read failure — return what we have
        }

        return results;
    }

    public void CleanupOldEntries(TimeSpan maxAge)
    {
        if (!_fileSystem.DirectoryExists(_archiveDir))
            return;

        try
        {
            var files = _fileSystem.GetFiles(_archiveDir, "*.json", SearchOption.TopDirectoryOnly);
            var cutoff = DateTime.UtcNow - maxAge;

            foreach (var file in files)
            {
                try
                {
                    var json = _fileSystem.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<SessionArchiveEntry>(json);
                    if (entry != null && entry.ArchivedAt < cutoff)
                        _fileSystem.DeleteFile(file);
                }
                catch
                {
                    // If we can't read it, delete it
                    try { _fileSystem.DeleteFile(file); } catch { }
                }
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

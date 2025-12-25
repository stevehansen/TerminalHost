using System.IO;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services
{
    public sealed class StatisticsService : IStatisticsService
    {
        private UsageStats _stats;
        private readonly string _statsPath;
        private bool _isDirty = false;
        private readonly System.Threading.Timer _saveTimer;
        private readonly object _lock = new();
        private bool _disposed = false;
        private readonly IFileSystem _fileSystem;
        private readonly JsonFileService<UsageStats> _jsonFileService;

        private static readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public StatisticsService(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _statsPath = GetStatsPath(fileSystem);
            _jsonFileService = new JsonFileService<UsageStats>(_fileSystem, _statsPath, options);
            _stats = LoadStats();
            // Save every 30 seconds if there are changes
            _saveTimer = new System.Threading.Timer(_ => SaveStatsIfNeeded(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private static string GetStatsPath(IFileSystem fileSystem)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var statsDir = Path.Combine(appData, "TerminalHost");
            fileSystem.CreateDirectory(statsDir);
            return Path.Combine(statsDir, "stats.json");
        }

        private UsageStats LoadStats()
        {
            return _jsonFileService.Load();
        }

        public void IncrementCharCount(string directory, string terminalType, int charCount)
        {
            if (string.IsNullOrEmpty(directory) || charCount <= 0) return;

            lock (_lock)
            {
                if (!_stats.DirectoryStats.TryGetValue(directory, out DirectoryUsageStats? dirStats))
                {
                    dirStats = new DirectoryUsageStats();
                    _stats.DirectoryStats[directory] = dirStats;
                }

                var today = DateTime.Now.ToString("yyyy-MM-dd");

                Dictionary<string, long> dailyCounts;

                switch (terminalType)
                {
                    case "Custom":
                        dailyCounts = dirStats.CustomTerminalCharCountsByDay;
                        break;
                    case "Shell":
                        dailyCounts = dirStats.ShellTerminalCharCountsByDay;
                        break;
                    case "Run":
                        dailyCounts = dirStats.RunTerminalCharCountsByDay;
                        break;
                    default:
                        return; // Or handle error
                }

                if (dailyCounts.ContainsKey(today))
                {
                    dailyCounts[today] += charCount;
                }
                else
                {
                    dailyCounts[today] = charCount;
                }

                _isDirty = true;
            }
        }

        public UsageStats GetStats()
        {
            lock (_lock)
            {
                // Return a deep copy to ensure thread safety with the UI
                var json = JsonSerializer.Serialize(_stats);
                return JsonSerializer.Deserialize<UsageStats>(json) ?? new UsageStats();
            }
        }

        private void SaveStatsIfNeeded()
        {
            if (_isDirty && !_disposed)
            {
                SaveStats();
            }
        }

        public void SaveStats()
        {
            lock (_lock)
            {
                if (!_isDirty) return;
                _isDirty = false;

                try
                {
                    _jsonFileService.Save(_stats);
                }
                catch (Exception ex)
                {
                    // Don't crash the app if stats saving fails, log it instead
                    System.Diagnostics.Debug.WriteLine($"[StatisticsService] Failed to save stats: {ex.Message}");
                }
            }
        }

        public void RecordFocusTime(string directory, int seconds)
        {
            if (string.IsNullOrEmpty(directory) || seconds <= 0) return;

            lock (_lock)
            {
                if (!_stats.DirectoryStats.TryGetValue(directory, out DirectoryUsageStats? dirStats))
                {
                    dirStats = new DirectoryUsageStats();
                    _stats.DirectoryStats[directory] = dirStats;
                }

                var today = DateTime.Now.ToString("yyyy-MM-dd");

                if (dirStats.FocusTimeSecondsByDay.ContainsKey(today))
                {
                    dirStats.FocusTimeSecondsByDay[today] += seconds;
                }
                else
                {
                    dirStats.FocusTimeSecondsByDay[today] = seconds;
                }

                _isDirty = true;
            }
        }

        public long GetFocusTimeForPeriod(string directory, int days)
        {
            lock (_lock)
            {
                if (!_stats.DirectoryStats.TryGetValue(directory, out DirectoryUsageStats? dirStats))
                    return 0;

                var cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");
                return dirStats.FocusTimeSecondsByDay
                    .Where(kv => string.Compare(kv.Key, cutoff, StringComparison.Ordinal) >= 0)
                    .Sum(kv => kv.Value);
            }
        }

        public long GetCharCountForPeriod(string directory, int days)
        {
            lock (_lock)
            {
                if (!_stats.DirectoryStats.TryGetValue(directory, out DirectoryUsageStats? dirStats))
                    return 0;

                var cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");

                var custom = dirStats.CustomTerminalCharCountsByDay
                    .Where(kv => string.Compare(kv.Key, cutoff, StringComparison.Ordinal) >= 0)
                    .Sum(kv => kv.Value);

                var shell = dirStats.ShellTerminalCharCountsByDay
                    .Where(kv => string.Compare(kv.Key, cutoff, StringComparison.Ordinal) >= 0)
                    .Sum(kv => kv.Value);

                var run = dirStats.RunTerminalCharCountsByDay
                    .Where(kv => string.Compare(kv.Key, cutoff, StringComparison.Ordinal) >= 0)
                    .Sum(kv => kv.Value);

                return custom + shell + run;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _saveTimer?.Dispose();
            // Block and wait for the final save to complete.
            SaveStats();
        }
    }
}

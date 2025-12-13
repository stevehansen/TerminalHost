using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Domain;

namespace TerminalHost.Services
{
    public class StatisticsService : IDisposable
    {
        private UsageStats _stats;
        private readonly string _statsPath;
        private bool _isDirty = false;
        private readonly System.Threading.Timer _saveTimer;
        private readonly object _lock = new object();
        private bool _disposed = false;

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true, };

        public StatisticsService()
        {
            _statsPath = GetStatsPath();
            _stats = LoadStats();
            // Save every 30 seconds if there are changes
            _saveTimer = new System.Threading.Timer(_ => SaveStatsIfNeeded(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private static string GetStatsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var statsDir = Path.Combine(appData, "TerminalHost");
            Directory.CreateDirectory(statsDir);
            return Path.Combine(statsDir, "stats.json");
        }

        private UsageStats LoadStats()
        {
            if (!File.Exists(_statsPath))
            {
                return new UsageStats();
            }

            try
            {
                var json = File.ReadAllText(_statsPath);
                return JsonSerializer.Deserialize<UsageStats>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new UsageStats();
            }
            catch (Exception)
            {
                // If file is corrupt, start fresh
                return new UsageStats();
            }
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
            }

            try
            {
                var json = JsonSerializer.Serialize(_stats, options);
                File.WriteAllText(_statsPath, json);
            }
            catch (Exception)
            {
                // Don't crash the app if stats saving fails
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

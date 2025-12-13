using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Services;

namespace TerminalHost.ViewModels
{
    public partial class StatisticsTabViewModel : ObservableObject, ITabViewModel
    {
        private readonly StatisticsService _statisticsService;

        public string Title => "Statistics";
        public string TabIcon => "📊";
        public bool IsCloseable => true;
        public string WorkingDirectory => string.Empty;
        public bool IsAnyTerminalActive => false;

        [ObservableProperty]
        private ObservableCollection<ProjectStatViewModel> _projectStats = new();

        public event EventHandler? CloseRequested;

        public StatisticsTabViewModel(StatisticsService statisticsService)
        {
            _statisticsService = statisticsService;
            LoadStats();
        }

        [RelayCommand]
        private void LoadStats()
        {
            var stats = _statisticsService.GetStats();
            var statsList = stats.DirectoryStats
                .Select(kvp =>
                {
                    var customTotal = kvp.Value.CustomTerminalCharCountsByDay.Values.Sum();
                    var shellTotal = kvp.Value.ShellTerminalCharCountsByDay.Values.Sum();
                    var runTotal = kvp.Value.RunTerminalCharCountsByDay.Values.Sum();
                    var total = customTotal + shellTotal + runTotal;

                    return new ProjectStatViewModel(
                        kvp.Key,
                        total,
                        customTotal,
                        shellTotal,
                        runTotal
                    );
                })
                .Where(s => s.TotalChars > 0)
                .OrderByDescending(s => s.TotalChars)
                .ToList();

            var maxChars = statsList.Any() ? statsList.Max(s => s.TotalChars) : 0;

            // Assuming max bar width is around 400 for now. This could be passed from the view.
            const double availableWidth = 400;
            foreach (var stat in statsList)
            {
                stat.CalculateBarWidths(maxChars, availableWidth);
            }

            ProjectStats = new ObservableCollection<ProjectStatViewModel>(statsList);
        }

        [RelayCommand]
        private void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
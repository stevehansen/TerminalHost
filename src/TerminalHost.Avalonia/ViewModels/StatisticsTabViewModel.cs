using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.ViewModels
{
    public partial class StatisticsTabViewModel : ObservableObject, ITabViewModel
    {
        private readonly IStatisticsService _statisticsService;

        public string Title => "Statistics";
        public string TabIcon => "📊";
        public bool IsCloseable => true;
        public bool CanDuplicate => false;
        public string WorkingDirectory => string.Empty;
        public bool IsAnyTerminalActive => false;
        public bool HasUnreadActivity => false;
        public bool IsSelected { get; set; }
        public bool IsVisibleInFocusMode => true; // Statistics always visible
        public bool ShowActivitySpinner => false; // No terminal activity
        public bool ShowCompletedIndicator => false; // No terminal activity
        public bool IsTerminalInitialized => true; // No terminal to initialize
        public Task InitializeTerminalsAsync() => Task.CompletedTask; // No-op
        public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { }
        public void ClearUnreadActivity() { }
        public string DisplayTitle => Title;

        [ObservableProperty]
        private ObservableCollection<ProjectStatViewModel> _projectStats = [];
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsChartVisible))]
        private ProjectStatViewModel? _selectedProject;

        [ObservableProperty]
        private ISeries[] _series = Array.Empty<ISeries>();

        [ObservableProperty]
        private Axis[] _xAxes = Array.Empty<Axis>();

        [ObservableProperty]
        private Axis[] _yAxes = Array.Empty<Axis>();

        public bool IsChartVisible => SelectedProject != null;


        public event EventHandler? CloseRequested;

        public StatisticsTabViewModel(IStatisticsService statisticsService)
        {
            _statisticsService = statisticsService;
            LoadStats();
            
            Series =
            [
                new ColumnSeries<long> { Name = "Custom" },
                new ColumnSeries<long> { Name = "Shell" },
                new ColumnSeries<long> { Name = "Run" }
            ];
            XAxes =
            [
                new Axis { Labels = [] }
            ];
            YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.White)
                }
            ];
        }

        partial void OnSelectedProjectChanged(ProjectStatViewModel? value)
        {
            if (value == null)
            {
                return;
            }

            UpdateChart(value.DailyStats);
        }

        private void UpdateChart(DirectoryUsageStats dailyStats)
        {
            var today = DateTime.UtcNow.Date;
            var dates = Enumerable.Range(0, 30).Select(i => today.AddDays(-i)).Reverse().ToList();

            var customData = new List<long>();
            var shellData = new List<long>();
            var runData = new List<long>();
            var labels = new List<string>();

            foreach (var date in dates)
            {
                var dateKey = date.ToString("yyyy-MM-dd");
                dailyStats.CustomTerminalCharCountsByDay.TryGetValue(dateKey, out var customCount);
                dailyStats.ShellTerminalCharCountsByDay.TryGetValue(dateKey, out var shellCount);
                dailyStats.RunTerminalCharCountsByDay.TryGetValue(dateKey, out var runCount);

                customData.Add(customCount);
                shellData.Add(shellCount);
                runData.Add(runCount);
                labels.Add(date.ToString("MMM dd"));
            }

            Series =
            [
                new ColumnSeries<long>
                {
                    Name = "Custom",
                    Values = customData,
                    Fill = new SolidColorPaint(SKColor.Parse("#9CDCFE")),
                    Stroke = null
                },
                new ColumnSeries<long>
                {
                    Name = "Shell",
                    Values = shellData,
                    Fill = new SolidColorPaint(SKColor.Parse("#CE9178")),
                    Stroke = null
                },
                new ColumnSeries<long>
                {
                    Name = "Run",
                    Values = runData,
                    Fill = new SolidColorPaint(SKColor.Parse("#B5CEA8")),
                    Stroke = null
                }
            ];
            
            XAxes =
            [
                new Axis
                {
                    Labels = labels,
                    LabelsRotation = 45,
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.White)
                }
            ];
        }


        [RelayCommand]
        private void LoadStats()
        {
            var stats = _statisticsService.GetStats();
            var statsList = stats.DirectoryStats
                .Select(kvp => new ProjectStatViewModel(kvp.Key, kvp.Value))
                .Where(s => s.TotalChars > 0)
                .OrderByDescending(s => s.TotalChars)
                .ToList();

            // Fixed width for bars: 420px column - 20px margins - 20px padding = 380px
            const double availableWidth = 375;
            foreach (var stat in statsList)
            {
                stat.CalculateBarWidths(availableWidth);
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
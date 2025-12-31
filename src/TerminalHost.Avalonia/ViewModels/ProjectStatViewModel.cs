using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHost.Domain;

namespace TerminalHost.ViewModels
{
    public partial class ProjectStatViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _projectName = "";

        [ObservableProperty]
        private long _totalChars;

        [ObservableProperty]
        private long _customChars;

        [ObservableProperty]
        private long _shellChars;

        [ObservableProperty]
        private long _runChars;

        [ObservableProperty]
        private double _customBarWidth;

        [ObservableProperty]
        private double _shellBarWidth;

        [ObservableProperty]
        private double _runBarWidth;

        public DirectoryUsageStats DailyStats { get; }

        public ProjectStatViewModel(string directoryPath, DirectoryUsageStats dailyStats)
        {
            ProjectName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            
            CustomChars = dailyStats.CustomTerminalCharCountsByDay.Values.Sum();
            ShellChars = dailyStats.ShellTerminalCharCountsByDay.Values.Sum();
            RunChars = dailyStats.RunTerminalCharCountsByDay.Values.Sum();
            TotalChars = CustomChars + ShellChars + RunChars;
            
            DailyStats = dailyStats;
        }

        public string TotalCharsFormatted => $"{TotalChars:N0}";
        public string CustomCharsFormatted => $"{CustomChars:N0}";
        public string ShellCharsFormatted => $"{ShellChars:N0}";
        public string RunCharsFormatted => $"{RunChars:N0}";

        public double CustomPercentage => TotalChars > 0 ? (double)CustomChars / TotalChars : 0;
        public double ShellPercentage => TotalChars > 0 ? (double)ShellChars / TotalChars : 0;
        public double RunPercentage => TotalChars > 0 ? (double)RunChars / TotalChars : 0;
        
        public void CalculateBarWidths(double availableWidth)
        {
            if (TotalChars <= 0)
            {
                CustomBarWidth = 0;
                ShellBarWidth = 0;
                RunBarWidth = 0;
                return;
            }

            // Each project fills the full width, showing internal proportions
            CustomBarWidth = availableWidth * CustomPercentage;
            ShellBarWidth = availableWidth * ShellPercentage;
            RunBarWidth = availableWidth * RunPercentage;
        }
    }
}
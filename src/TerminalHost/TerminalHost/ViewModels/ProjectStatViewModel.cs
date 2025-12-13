using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

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

        public ProjectStatViewModel(string directoryPath, long totalChars, long customChars, long shellChars, long runChars)
        {
            ProjectName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            TotalChars = totalChars;
            CustomChars = customChars;
            ShellChars = shellChars;
            RunChars = runChars;
        }

        public string TotalCharsFormatted => $"{TotalChars:N0}";
        public string CustomCharsFormatted => $"{CustomChars:N0}";
        public string ShellCharsFormatted => $"{ShellChars:N0}";
        public string RunCharsFormatted => $"{RunChars:N0}";

        public double CustomPercentage => TotalChars > 0 ? (double)CustomChars / TotalChars : 0;
        public double ShellPercentage => TotalChars > 0 ? (double)ShellChars / TotalChars : 0;
        public double RunPercentage => TotalChars > 0 ? (double)RunChars / TotalChars : 0;

        public void CalculateBarWidths(long maxCharsInSet, double availableWidth)
        {
            if (maxCharsInSet <= 0 || TotalChars <= 0)
            {
                CustomBarWidth = 0;
                ShellBarWidth = 0;
                RunBarWidth = 0;
                return;
            }

            double totalBarWidth = (double)TotalChars / maxCharsInSet * availableWidth;
            CustomBarWidth = totalBarWidth * CustomPercentage;
            ShellBarWidth = totalBarWidth * ShellPercentage;
            RunBarWidth = totalBarWidth * RunPercentage;
        }
    }
}
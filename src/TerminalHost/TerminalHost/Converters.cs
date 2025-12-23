using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TerminalHost.Core.Domain;
using TerminalHost.Core.ViewModels;

namespace TerminalHost;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        // For object null checks
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility == Visibility.Visible;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        // For object null checks
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility == Visibility.Collapsed;
    }
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to a color for the status indicator.
/// </summary>
public class RunStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush StoppedBrush = new(Color.FromRgb(128, 128, 128));   // Gray
    private static readonly SolidColorBrush StartingBrush = new(Color.FromRgb(255, 200, 0));   // Yellow/Amber
    private static readonly SolidColorBrush RunningBrush = new(Color.FromRgb(50, 205, 50));    // Lime Green
    private static readonly SolidColorBrush StoppingBrush = new(Color.FromRgb(255, 165, 0));   // Orange

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RunState state)
        {
            return state switch
            {
                RunState.Stopped => StoppedBrush,
                RunState.Starting => StartingBrush,
                RunState.Running => RunningBrush,
                RunState.Stopping => StoppingBrush,
                _ => StoppedBrush
            };
        }
        return StoppedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to an icon character for the run button.
/// </summary>
public class RunStateToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RunState state)
        {
            return state switch
            {
                RunState.Stopped => "▶",     // Play
                RunState.Starting => "⏳",    // Hourglass
                RunState.Running => "⏹",     // Stop
                RunState.Stopping => "⏳",    // Hourglass
                _ => "▶"
            };
        }
        return "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to a tooltip string.
/// </summary>
public class RunStateToTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RunState state)
        {
            return state switch
            {
                RunState.Stopped => "Start (F5)",
                RunState.Starting => "Starting...",
                RunState.Running => "Stop (Shift+F5)",
                RunState.Stopping => "Stopping...",
                _ => "Run"
            };
        }
        return "Run";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null/empty string to Visibility.Collapsed.
/// Use ConverterParameter=Invert to invert the logic (show when null, hide when not null).
/// </summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNullOrEmpty;
        if (value is string str)
        {
            isNullOrEmpty = string.IsNullOrEmpty(str);
        }
        else
        {
            isNullOrEmpty = value == null;
        }

        // Check for Invert parameter
        bool invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            isNullOrEmpty = !isNullOrEmpty;
        }

        return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a GridLength of 4 for true, 0 for false (for splitter visibility).
/// </summary>
public class BoolToSplitterWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isVisible)
        {
            return isVisible ? new GridLength(4) : new GridLength(0);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts null to Visible, non-null to Collapsed (for showing placeholders).
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null to true, non-null to false.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts 0 to Visible, non-zero to Collapsed (for empty state).
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a Color object.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                return Colors.Gray;
            }
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a full path to just the folder name.
/// </summary>
public class PathToFolderNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            return System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) ?? path;
        }
        return value ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a SolidColorBrush for UI binding.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorHex && !string.IsNullOrEmpty(colorHex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return DefaultBrush;
            }
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to RowSpan: true = 3 (span all rows), false = 1.
/// Used for horizontal split mode where terminals span all rows.
/// </summary>
public class BoolToRowSpanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isHorizontalMode)
        {
            return isHorizontalMode ? 3 : 1;
        }
        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to ColumnSpan: true = 3 (span all columns), false = 1.
/// Used for vertical split mode where shell terminal spans all columns.
/// </summary>
public class BoolToColumnSpanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 3 : 1;
        }
        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool (IsVerticalSplitMode) to Shell terminal column:
/// true (vertical) = 0, false (horizontal) = 2.
/// </summary>
public class BoolToShellColumnConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 0 : 2;
        }
        return 2;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool (IsVerticalSplitMode) to Shell terminal row:
/// true (vertical) = 2, false (horizontal) = 0.
/// </summary>
public class BoolToShellRowConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 2 : 0;
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts SettingsViewMode enum to boolean for RadioButton binding.
/// ConverterParameter specifies the target mode (Rich or Raw).
/// </summary>
public class ViewModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SettingsViewMode mode && parameter is string targetModeStr)
        {
            if (Enum.TryParse<SettingsViewMode>(targetModeStr, out var targetMode))
            {
                return mode == targetMode;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetModeStr)
        {
            if (Enum.TryParse<SettingsViewMode>(targetModeStr, out var targetMode))
            {
                return targetMode;
            }
        }
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// Converts SettingsViewMode to Visibility based on TargetMode property.
/// </summary>
public class ViewModeToVisibilityConverter : IValueConverter
{
    public SettingsViewMode TargetMode { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SettingsViewMode mode)
        {
            return mode == TargetMode ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts TerminalLayoutMode enum to boolean for RadioButton binding.
/// ConverterParameter specifies the target layout mode.
/// </summary>
public class LayoutModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TerminalLayoutMode mode && parameter is string targetModeStr)
        {
            if (Enum.TryParse<TerminalLayoutMode>(targetModeStr, out var targetMode))
            {
                return mode == targetMode;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetModeStr)
        {
            if (Enum.TryParse<TerminalLayoutMode>(targetModeStr, out var targetMode))
            {
                return targetMode;
            }
        }
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// Converts SettingsSection enum to boolean for RadioButton binding.
/// ConverterParameter specifies the target section.
/// </summary>
public class SectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SettingsSection section && parameter is string targetSectionStr)
        {
            if (Enum.TryParse<SettingsSection>(targetSectionStr, out var targetSection))
            {
                return section == targetSection;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetSectionStr)
        {
            if (Enum.TryParse<SettingsSection>(targetSectionStr, out var targetSection))
            {
                return targetSection;
            }
        }
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// Converts SettingsSection to Visibility.
/// ConverterParameter specifies which section should be visible.
/// </summary>
public class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SettingsSection section && parameter is string targetSectionStr)
        {
            if (Enum.TryParse<SettingsSection>(targetSectionStr, out var targetSection))
            {
                return section == targetSection ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to a background brush for selected state.
/// True = subtle highlight, False = transparent.
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromArgb(40, 255, 255, 255));  // Semi-transparent white
    private static readonly SolidColorBrush TransparentBrush = System.Windows.Media.Brushes.Transparent;

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            return SelectedBrush;
        }
        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to FontWeight: true = SemiBold, false = Normal.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            return FontWeights.SemiBold;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to expand/collapse text: true = "▼", false = "▶".
/// </summary>
public class BoolToExpandCollapseTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "▼" : "▶";
        }
        return "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Truncates a string to a specified length with ellipsis.
/// ConverterParameter specifies the max length (default 50).
/// </summary>
public class TruncateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string str)
            return "";

        int maxLength = 50;
        if (parameter is string paramStr && int.TryParse(paramStr, out var parsed))
        {
            maxLength = parsed;
        }

        // Replace newlines with spaces for preview
        str = str.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        if (str.Length <= maxLength)
            return str;

        return str[..maxLength] + "...";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts int to Visibility: > 0 = Visible, 0 = Collapsed.
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Compares two objects for equality. Returns true if they are equal.
/// Used for highlighting the active panel tab.
/// </summary>
public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        return Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a DateTime to a relative time string (e.g., "2 hours ago", "3 days ago").
/// </summary>
public class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime)
            return "";

        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;

        // Handle both local and UTC times
        var diff = dateTime.Kind == DateTimeKind.Utc
            ? utcNow - dateTime
            : now - dateTime;

        if (diff.TotalSeconds < 60)
            return "just now";

        if (diff.TotalMinutes < 60)
        {
            var mins = (int)diff.TotalMinutes;
            return mins == 1 ? "1 minute ago" : $"{mins} minutes ago";
        }

        if (diff.TotalHours < 24)
        {
            var hours = (int)diff.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (diff.TotalDays < 7)
        {
            var days = (int)diff.TotalDays;
            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        if (diff.TotalDays < 30)
        {
            var weeks = (int)(diff.TotalDays / 7);
            return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";
        }

        if (diff.TotalDays < 365)
        {
            var months = (int)(diff.TotalDays / 30);
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        var years = (int)(diff.TotalDays / 365);
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

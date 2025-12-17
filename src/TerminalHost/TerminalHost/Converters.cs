using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

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
    private static readonly SolidColorBrush StoppedBrush = new(System.Windows.Media.Color.FromRgb(128, 128, 128));   // Gray
    private static readonly SolidColorBrush StartingBrush = new(System.Windows.Media.Color.FromRgb(255, 200, 0));   // Yellow/Amber
    private static readonly SolidColorBrush RunningBrush = new(System.Windows.Media.Color.FromRgb(50, 205, 50));    // Lime Green
    private static readonly SolidColorBrush StoppingBrush = new(System.Windows.Media.Color.FromRgb(255, 165, 0));   // Orange

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

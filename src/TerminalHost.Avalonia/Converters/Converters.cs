using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Converters;

/// <summary>
/// Converts a boolean to a visibility boolean (true = visible, false = hidden).
/// In Avalonia, bind to IsVisible directly.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }
        // For object null checks
        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool visible && visible;
    }
}

/// <summary>
/// Converts a boolean to inverted visibility (true = hidden, false = visible).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        // For object null checks
        return value == null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool visible && !visible;
    }
}

/// <summary>
/// Converts count > 0 to visible (true).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to an icon character for the run button.
/// </summary>
public class RunStateToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RunState state)
        {
            return state switch
            {
                RunState.Stopped => "\u25B6",     // Play (Γû╢)
                RunState.Starting => "\u23F3",    // Hourglass (ΓÅ│)
                RunState.Running => "\u23F9",     // Stop (ΓÅ╣)
                RunState.Stopping => "\u23F3",    // Hourglass (ΓÅ│)
                _ => "\u25B6"
            };
        }
        return "\u25B6";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to a tooltip string.
/// </summary>
public class RunStateToTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null/empty string to hidden (false for IsVisible).
/// Use ConverterParameter=Invert to invert the logic.
/// </summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

        return !isNullOrEmpty; // Return true (visible) if NOT null/empty
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a GridLength of 4 for true, 0 for false (for splitter visibility).
/// </summary>
public class BoolToSplitterWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible)
        {
            return isVisible ? new GridLength(4) : new GridLength(0);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts null to hidden, non-null to visible.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null to true, non-null to false.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts 0 to visible, non-zero to hidden (for empty state).
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a Color object.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            try
            {
                return Color.Parse(colorString);
            }
            catch
            {
                return Colors.Gray;
            }
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a full path to just the folder name.
/// </summary>
public class PathToFolderNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            return Path.GetFileName(path.TrimEnd('\\', '/')) ?? path;
        }
        return value ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a SolidColorBrush for UI binding.
/// Returns null (transparent) for null/empty values.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorHex && !string.IsNullOrEmpty(colorHex))
        {
            try
            {
                var color = Color.Parse(colorHex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return null; // Return transparent for invalid colors
            }
        }
        return null; // Return transparent (null brush) for null/empty values
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to RowSpan: true = 3 (span all rows), false = 1.
/// </summary>
public class BoolToRowSpanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isHorizontalMode)
        {
            return isHorizontalMode ? 3 : 1;
        }
        return 1;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to ColumnSpan: true = 3 (span all columns), false = 1.
/// </summary>
public class BoolToColumnSpanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 3 : 1;
        }
        return 1;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 0 : 2;
        }
        return 2;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVerticalMode)
        {
            return isVerticalMode ? 2 : 0;
        }
        return 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to a background brush for selected state.
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromArgb(40, 255, 255, 255));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            return SelectedBrush;
        }
        return TransparentBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to FontWeight: true = SemiBold, false = Normal.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            return FontWeight.SemiBold;
        }
        return FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to expand/collapse text.
/// </summary>
public class BoolToExpandCollapseTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "\u25BC" : "\u25B6"; // Γû╝ : Γû╢
        }
        return "\u25B6";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Truncates a string to a specified length with ellipsis.
/// </summary>
public class TruncateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts numeric values > 0 to visible (true).
/// Supports int, long, double, and float.
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int intValue => intValue > 0,
            long longValue => longValue > 0,
            double doubleValue => doubleValue > 0,
            float floatValue => floatValue > 0,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a DateTime to a relative time string.
/// </summary>
public class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime)
            return "";

        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;

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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts DiffLineType to a background brush for diff rows.
/// Parameter: "Left" or "Right" to specify which side.
/// </summary>
public class DiffLineTypeToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush HunkHeaderBrush = new(Color.Parse("#2D2D30"));
    private static readonly SolidColorBrush AdditionBrush = new(Color.Parse("#233623"));
    private static readonly SolidColorBrush DeletionBrush = new(Color.Parse("#3E1E1E"));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Check if this is a hunk header row first
        if (value is bool isHunkHeader && isHunkHeader)
        {
            return HunkHeaderBrush;
        }

        // Otherwise check the diff line type
        if (value is DiffLineType lineType)
        {
            return lineType switch
            {
                DiffLineType.Addition => AdditionBrush,
                DiffLineType.Deletion => DeletionBrush,
                _ => TransparentBrush
            };
        }

        return TransparentBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts DiffLineType to a foreground brush for diff content.
/// </summary>
public class DiffLineTypeToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush HunkHeaderBrush = new(Color.Parse("#569CD6"));
    private static readonly SolidColorBrush AdditionBrush = new(Color.Parse("#6A9955"));
    private static readonly SolidColorBrush DeletionBrush = new(Color.Parse("#F14C4C"));
    private static readonly SolidColorBrush DefaultBrush = new(Color.Parse("#CCCCCC"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Check if this is a hunk header row first
        if (value is bool isHunkHeader && isHunkHeader)
        {
            return HunkHeaderBrush;
        }

        // Otherwise check the diff line type
        if (value is DiffLineType lineType)
        {
            return lineType switch
            {
                DiffLineType.Addition => AdditionBrush,
                DiffLineType.Deletion => DeletionBrush,
                _ => DefaultBrush
            };
        }

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Multi-value converter that returns line number text based on whether the row is a hunk header.
/// Values: [0] = IsHunkHeader (bool), [1] = LineNumber (int?)
/// Returns "..." for hunk headers, line number otherwise.
/// </summary>
public class DiffLineNumberConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return "";

        // First value is IsHunkHeader
        if (values[0] is bool isHunkHeader && isHunkHeader)
        {
            return "...";
        }

        // Second value is the line number
        if (values[1] is int lineNumber)
        {
            return lineNumber.ToString();
        }

        return "";
    }
}

/// <summary>
/// Multi-value converter that returns content text based on whether the row is a hunk header.
/// Values: [0] = IsHunkHeader (bool), [1] = HunkHeaderText (string), [2] = Content (string)
/// Returns HunkHeaderText for hunk headers, Content otherwise.
/// </summary>
public class DiffContentConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3)
            return "";

        // First value is IsHunkHeader
        if (values[0] is bool isHunkHeader && isHunkHeader)
        {
            // Second value is HunkHeaderText
            return values[1]?.ToString() ?? "";
        }

        // Third value is the regular content
        return values[2]?.ToString() ?? "";
    }
}

/// <summary>
/// Converts SettingsViewMode enum to boolean for RadioButton binding.
/// ConverterParameter specifies the target mode (Rich or Raw).
/// </summary>
public class ViewModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetModeStr)
        {
            if (Enum.TryParse<SettingsViewMode>(targetModeStr, out var targetMode))
            {
                return targetMode;
            }
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts SettingsViewMode to bool based on TargetMode property.
/// </summary>
public class ViewModeToVisibilityConverter : IValueConverter
{
    public SettingsViewMode TargetMode { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SettingsViewMode mode)
        {
            return mode == TargetMode;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetModeStr)
        {
            if (Enum.TryParse<TerminalLayoutMode>(targetModeStr, out var targetMode))
            {
                return targetMode;
            }
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts SettingsSection enum to boolean for RadioButton binding.
/// ConverterParameter specifies the target section.
/// </summary>
public class SectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetSectionStr)
        {
            if (Enum.TryParse<SettingsSection>(targetSectionStr, out var targetSection))
            {
                return targetSection;
            }
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts SettingsSection to bool (for IsVisible).
/// ConverterParameter specifies which section should be visible.
/// </summary>
public class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to one of two strings.
/// ConverterParameter format: "TrueValue|FalseValue"
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                return boolValue ? parts[0] : parts[1];
            }
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to one of two brushes specified by hex color.
/// ConverterParameter format: "TrueColor|FalseColor" (e.g., "#2196F3|#444444")
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                var colorHex = boolValue ? parts[0] : parts[1];
                try
                {
                    return new SolidColorBrush(Color.Parse(colorHex));
                }
                catch
                {
                    return new SolidColorBrush(Colors.Transparent);
                }
            }
        }
        // Default: return a selected background when true, transparent when false
        if (value is bool selected)
        {
            return selected
                ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                : new SolidColorBrush(Colors.Transparent);
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a Color object.
/// Similar to StringToColorConverter but specifically for hex strings.
/// </summary>
public class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorHex && !string.IsNullOrEmpty(colorHex))
        {
            try
            {
                return Color.Parse(colorHex);
            }
            catch
            {
                return Colors.Gray;
            }
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Alias for BoolToExpandCollapseTextConverter for Timeline Mode.
/// Converts bool to expand/collapse icon.
/// </summary>
public class BoolToExpanderIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "\u25BC" : "\u25B6"; // Γû╝ : Γû╢
        }
        return "\u25B6";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
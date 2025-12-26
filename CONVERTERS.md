# XAML Value Converters Reference

Quick reference for all converters defined in `src/TerminalHost/TerminalHost/Converters.cs`.

## Visibility Converters

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `BoolToVisibilityConverter` | `bool` or object | Visible/Collapsed | - |
| `InverseBoolToVisibilityConverter` | `bool` or object | Collapsed/Visible | - |
| `CountToVisibilityConverter` | `int` | >0=Visible, 0=Collapsed | - |
| `ZeroToVisibilityConverter` | `int` | 0=Visible, >0=Collapsed | - |
| `IntToVisibilityConverter` | `int` | >0=Visible, 0=Collapsed | - |
| `NullToVisibilityConverter` | object | non-null=Visible | - |
| `NullToCollapsedConverter` | object/string | null=Collapsed | `"Invert"` to reverse |
| `SectionToVisibilityConverter` | `SettingsSection` | Visibility | Section name string |

## Bool Converters

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `InverseBoolConverter` | `bool` | inverted `bool` | - |
| `BoolToStringConverter` | `bool` | string | `"TrueVal\|FalseVal"` |
| `BoolToBackgroundConverter` | `bool` | Brush (highlight/transparent) | - |
| `BoolToFontWeightConverter` | `bool` | SemiBold/Normal | - |
| `BoolToExpandCollapseTextConverter` | `bool` | "▼"/"▶" | - |
| `BoolToSplitterWidthConverter` | `bool` | GridLength(4)/GridLength(0) | - |
| `BoolToRowSpanConverter` | `bool` | 3/1 | - |
| `BoolToColumnSpanConverter` | `bool` | 3/1 | - |
| `BoolToShellColumnConverter` | `bool` | 0/2 | - |
| `BoolToShellRowConverter` | `bool` | 2/0 | - |
| `NullToBoolConverter` | object | `bool` (null=true) | - |

## String/Path Converters

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `TruncateConverter` | `string` | truncated string | max length (default 50) |
| `PathToFolderNameConverter` | path string | folder name only | - |
| `RelativeTimeConverter` | `DateTime` | "2 hours ago" etc. | - |

## Color Converters

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `StringToColorConverter` | hex string | `Color` | - |
| `HexToBrushConverter` | hex string | `SolidColorBrush` | - |

## Enum Converters (for RadioButtons)

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `ViewModeConverter` | `SettingsViewMode` | `bool` | Mode name string |
| `ViewModeToVisibilityConverter` | `SettingsViewMode` | Visibility | (uses TargetMode property) |
| `LayoutModeConverter` | `TerminalLayoutMode` | `bool` | Mode name string |
| `SectionConverter` | `SettingsSection` | `bool` | Section name string |

## RunState Converters

| Converter | Input | Output | Parameter |
|-----------|-------|--------|-----------|
| `RunStateToColorConverter` | `RunState` | `SolidColorBrush` | - |
| `RunStateToIconConverter` | `RunState` | icon string (▶/⏹/⏳) | - |
| `RunStateToTooltipConverter` | `RunState` | tooltip string | - |

## Multi-Value Converters

| Converter | Inputs | Output | Notes |
|-----------|--------|--------|-------|
| `EqualityConverter` | 2 objects | `bool` | true if equal |

## Usage Examples

```xml
<!-- Basic visibility -->
<Border Visibility="{Binding IsVisible, Converter={StaticResource BoolToVisibilityConverter}}"/>

<!-- Inverse visibility -->
<TextBlock Visibility="{Binding HasItems, Converter={StaticResource InverseBoolToVisibilityConverter}}"
           Text="No items"/>

<!-- Bool to custom strings -->
<TextBlock Text="{Binding IsExpanded, Converter={StaticResource BoolToStringConverter},
           ConverterParameter='▼|▶'}"/>

<!-- Null check with invert -->
<Border Visibility="{Binding SelectedItem, Converter={StaticResource NullToCollapsedConverter},
        ConverterParameter=Invert}"/>

<!-- Truncate with custom length -->
<TextBlock Text="{Binding Description, Converter={StaticResource TruncateConverter},
           ConverterParameter=30}"/>

<!-- Enum to bool for RadioButton -->
<RadioButton IsChecked="{Binding CurrentMode, Converter={StaticResource LayoutModeConverter},
             ConverterParameter=HorizontalSplit}"/>

<!-- Multi-value equality check -->
<Border.Background>
    <MultiBinding Converter="{StaticResource EqualityConverter}">
        <Binding Path="SelectedTab"/>
        <Binding Path="."/>
    </MultiBinding>
</Border.Background>
```

## Common Mistakes

- `BoolToVisibilityConverter` not ~~`BooleanToVisibilityConverter`~~
- `BoolToStringConverter` requires parameter format `"TrueValue|FalseValue"`
- `NullToCollapsedConverter` shows when NOT null (use `Invert` param to reverse)
- `ZeroToVisibilityConverter` shows when zero (opposite of `CountToVisibilityConverter`)

# Stage 5: Core UI Migration (Avalonia)

## Overview

| Attribute | Value |
|-----------|-------|
| **Status** | **COMPLETED** |
| **Completed Date** | 2025-12-22 |
| **Estimated Effort** | 7-10 days |
| **Risk Level** | **High** |
| **Dependencies** | Stages 1-4 complete |
| **Blocking For** | Stages 6, 7, 8 |

## Objective

Migrate the core application shell from WPF to Avalonia, including App.xaml, MainWindow.xaml, and all global resources. This establishes the foundation for all other view migrations.

## Success Criteria

- [x] Application launches with Avalonia
- [x] Main window displays correctly
- [x] Theme and colors apply
- [ ] Keyboard shortcuts work *(placeholder - needs ViewModels from Stage 6)*
- [ ] Tab management functional *(needs Stage 7 Views)*
- [x] DI services resolve correctly

## Implementation Notes

### Key Differences from Original Plan

1. **Assembly Name**: The assembly is named `host`, not `TerminalHost`, so `avares://` URLs use `avares://host/...`
2. **Resource Dictionaries**: Colors and Typography are ResourceDictionary files, not Styles, so they use `ResourceInclude` instead of `StyleInclude`
3. **Simplified MainWindow**: Stage 5 creates a placeholder MainWindow with terminal support; full tab/popup implementation requires Stages 6-7
4. **Files Preserved**: Old WPF files renamed to `.wpf.bak` instead of deleted (for reference during migration)
5. **Deferred Items**: Controls.axaml, Buttons.axaml, ScrollBars.axaml deferred until Stage 7 when control styles are needed

---

## XAML Syntax Differences: WPF vs Avalonia

### Quick Reference

| WPF | Avalonia |
|-----|----------|
| `xmlns="http://schemas.microsoft.com/..."` | `xmlns="https://github.com/avaloniaui"` |
| `Visibility="Collapsed"` | `IsVisible="False"` |
| `Visibility="{Binding ...}"` | `IsVisible="{Binding ...}"` |
| `Style.Triggers` | `Style Selector` with pseudo-classes |
| `DataTrigger` | `Classes` bindings |
| `InputBinding` | `KeyBinding` |
| `x:Type` | Direct type reference |
| `d:DataContext` | Same |
| `SystemParameters.*` | Custom or Avalonia APIs |

---

## Detailed Implementation

### 5.1 App.axaml

**DELETE:** `src/TerminalHost/TerminalHost/App.xaml`

**CREATE:** `src/TerminalHost/TerminalHost/App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:TerminalHost"
             x:Class="TerminalHost.App"
             RequestedThemeVariant="Dark">

    <Application.Styles>
        <FluentTheme />

        <!-- Global Styles -->
        <StyleInclude Source="avares://TerminalHost/Styles/Colors.axaml"/>
        <StyleInclude Source="avares://TerminalHost/Styles/Typography.axaml"/>
        <StyleInclude Source="avares://TerminalHost/Styles/Controls.axaml"/>
        <StyleInclude Source="avares://TerminalHost/Styles/Buttons.axaml"/>
        <StyleInclude Source="avares://TerminalHost/Styles/ScrollBars.axaml"/>
    </Application.Styles>

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://TerminalHost/Resources/Converters.axaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

---

### 5.2 App.axaml.cs

**DELETE:** `src/TerminalHost/TerminalHost/App.xaml.cs`

**CREATE:** `src/TerminalHost/TerminalHost/App.axaml.cs`

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace TerminalHost;

public partial class App : Application
{
    private ISingleInstanceService? _singleInstanceService;
    private IServiceProvider? _services;

    public new static App Current => (App)Application.Current!;
    public IServiceProvider Services => _services!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize LiveCharts
        LiveCharts.Configure(config =>
            config
                .AddSkiaSharp()
                .AddDefaultMappers()
                .AddDarkTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = CommandLineArgs.Parse(desktop.Args ?? Array.Empty<string>());

            // Handle setup mode
            if (args.IsSetupMode)
            {
                var setupWindow = new Views.SetupWindow();
                desktop.MainWindow = setupWindow;
                setupWindow.Show();
                return;
            }

            // Single instance handling
            _singleInstanceService = new SingleInstanceService();
            if (!args.DisableSingleInstance)
            {
                if (!_singleInstanceService.TryAcquireLock())
                {
                    if (args.HasValidRequest())
                    {
                        SingleInstanceService.SendToRunningInstance(args);
                    }
                    desktop.Shutdown();
                    return;
                }

                _singleInstanceService.StartPipeServer();
                _singleInstanceService.CommandReceived += OnCommandReceived;
            }

            // Configure DI
            var services = new ServiceCollection();
            ConfigureServices(services, args);
            _services = services.BuildServiceProvider();

            // Create main window
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // Handle shutdown
            desktop.ShutdownRequested += OnShutdownRequested;

            // Handle command line
            HandleCommandLineArgs(args);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services, CommandLineArgs args)
    {
        // Platform Services (new for macOS)
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();

        // Core Services
        services.AddSingleton(_singleInstanceService!);
        services.AddSingleton<IConfigurationService>(sp =>
            new ConfigurationService(sp.GetRequiredService<IFileSystem>(), args.UserDataDir));
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IProfileRegistry, ProfileRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();
        services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IGitStatusService, GitStatusService>();
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, RunUrlDetectionService>();
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();
        services.AddSingleton<IFileEditService, FileEditService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IClaudeCommandService, ClaudeCommandService>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDiffParserService, DiffParserService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TaskPanelViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<FileViewerViewModel>();
        services.AddSingleton<RepositorySwitcherViewModel>();
        services.AddSingleton<TestResultsViewModel>();
        services.AddSingleton<PrReviewViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();
        services.AddTransient<SetupViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    private void OnCommandReceived(object? sender, CommandLineArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            HandleCommandLineArgs(args);
            var mainWindow = Services.GetService<MainWindow>();
            mainWindow?.BringToFront();
        });
    }

    private void HandleCommandLineArgs(CommandLineArgs args)
    {
        var viewModel = Services.GetService<MainViewModel>();
        if (viewModel != null && !string.IsNullOrEmpty(args.WorkingDir))
        {
            viewModel.OpenProjectTab(args.WorkingDir);
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _services?.GetService<ISystemTrayService>()?.Dispose();
        _services?.GetService<ISingleInstanceService>()?.Dispose();
        _services?.GetService<IStatisticsService>()?.Dispose();
        (_services?.GetService<IClaudeCommandService>() as IDisposable)?.Dispose();
    }
}
```

---

### 5.3 Color Resources

**CREATE:** `src/TerminalHost/TerminalHost/Styles/Colors.axaml`

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Background Colors -->
    <Color x:Key="BackgroundColor">#1E1E1E</Color>
    <Color x:Key="SidebarBackground">#252526</Color>
    <Color x:Key="TabBackground">#2D2D2D</Color>
    <Color x:Key="TabBackgroundActive">#1E1E1E</Color>
    <Color x:Key="TabBackgroundHover">#383838</Color>
    <Color x:Key="InputBackground">#3C3C3C</Color>
    <Color x:Key="PopupBackground">#252526</Color>
    <Color x:Key="TooltipBackground">#1E1E1E</Color>

    <!-- Border Colors -->
    <Color x:Key="BorderColor">#3C3C3C</Color>
    <Color x:Key="BorderColorLight">#454545</Color>
    <Color x:Key="FocusBorderColor">#0078D4</Color>

    <!-- Text Colors -->
    <Color x:Key="TextColor">#CCCCCC</Color>
    <Color x:Key="TextColorSecondary">#808080</Color>
    <Color x:Key="TextColorDisabled">#5A5A5A</Color>
    <Color x:Key="TextColorBright">#FFFFFF</Color>

    <!-- Accent Colors -->
    <Color x:Key="AccentColor">#0078D4</Color>
    <Color x:Key="AccentColorHover">#1E8AD4</Color>
    <Color x:Key="AccentColorPressed">#006CBE</Color>

    <!-- Status Colors -->
    <Color x:Key="SuccessColor">#4EC9B0</Color>
    <Color x:Key="WarningColor">#CE9178</Color>
    <Color x:Key="ErrorColor">#F14C4C</Color>
    <Color x:Key="InfoColor">#3794FF</Color>

    <!-- Git Status Colors -->
    <Color x:Key="GitModifiedColor">#E2C08D</Color>
    <Color x:Key="GitAddedColor">#89D185</Color>
    <Color x:Key="GitDeletedColor">#C74E39</Color>
    <Color x:Key="GitUntrackedColor">#73C991</Color>
    <Color x:Key="GitConflictColor">#E51400</Color>

    <!-- Terminal Colors -->
    <Color x:Key="TerminalBackground">#0C0C0C</Color>
    <Color x:Key="TerminalForeground">#CCCCCC</Color>

    <!-- Brushes -->
    <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
    <SolidColorBrush x:Key="SidebarBackgroundBrush" Color="{StaticResource SidebarBackground}"/>
    <SolidColorBrush x:Key="TabBackgroundBrush" Color="{StaticResource TabBackground}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
    <SolidColorBrush x:Key="TextBrush" Color="{StaticResource TextColor}"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextColorSecondary}"/>
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}"/>

</ResourceDictionary>
```

---

### 5.4 Typography Resources

**CREATE:** `src/TerminalHost/TerminalHost/Styles/Typography.axaml`

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Font Families -->
    <!-- macOS fonts with fallbacks -->
    <FontFamily x:Key="FontFamilyDefault">SF Pro, -apple-system, Helvetica Neue, Arial, sans-serif</FontFamily>
    <FontFamily x:Key="FontFamilyMonospace">SF Mono, Menlo, Monaco, Cascadia Code, Consolas, monospace</FontFamily>

    <!-- Font Sizes -->
    <x:Double x:Key="FontSizeSmall">12</x:Double>
    <x:Double x:Key="FontSizeNormal">13</x:Double>
    <x:Double x:Key="FontSizeMedium">14</x:Double>
    <x:Double x:Key="FontSizeLarge">16</x:Double>
    <x:Double x:Key="FontSizeXLarge">20</x:Double>
    <x:Double x:Key="FontSizeHeader">24</x:Double>

    <!-- Font Weights -->
    <FontWeight x:Key="FontWeightLight">Light</FontWeight>
    <FontWeight x:Key="FontWeightNormal">Normal</FontWeight>
    <FontWeight x:Key="FontWeightMedium">Medium</FontWeight>
    <FontWeight x:Key="FontWeightSemiBold">SemiBold</FontWeight>
    <FontWeight x:Key="FontWeightBold">Bold</FontWeight>

</ResourceDictionary>
```

---

### 5.5 MainWindow.axaml

**CREATE:** `src/TerminalHost/TerminalHost/MainWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:TerminalHost.ViewModels"
        xmlns:views="using:TerminalHost.Views"
        mc:Ignorable="d"
        x:Class="TerminalHost.MainWindow"
        x:DataType="vm:MainViewModel"
        Title="TerminalHost"
        Width="1200" Height="800"
        MinWidth="800" MinHeight="600"
        Background="{StaticResource BackgroundBrush}">

    <Window.KeyBindings>
        <!-- Tab Navigation -->
        <KeyBinding Gesture="Ctrl+PageDown" Command="{Binding CycleTabCommand}" CommandParameter="True"/>
        <KeyBinding Gesture="Ctrl+PageUp" Command="{Binding CycleTabCommand}" CommandParameter="False"/>
        <KeyBinding Gesture="Ctrl+W" Command="{Binding CloseTabCommand}" CommandParameter="{Binding SelectedTab}"/>

        <!-- Application -->
        <KeyBinding Gesture="Ctrl+N" Command="{Binding OpenNewProjectCommand}"/>
        <KeyBinding Gesture="Ctrl+OemComma" Command="{Binding OpenSettingsCommand}"/>
        <KeyBinding Gesture="Ctrl+P" Command="{Binding OpenProfilesCommand}"/>
        <KeyBinding Gesture="Ctrl+E" Command="{Binding OpenInExplorerCommand}"/>

        <!-- Terminal -->
        <KeyBinding Gesture="Ctrl+OemTilde" Command="{Binding SwitchActiveTerminalCommand}"/>
    </Window.KeyBindings>

    <Grid RowDefinitions="Auto,*">
        <!-- Tab Strip -->
        <views:TabStrip Grid.Row="0"
                        DataContext="{Binding}"
                        Tabs="{Binding Tabs}"
                        SelectedTab="{Binding SelectedTab, Mode=TwoWay}"/>

        <!-- Content Area -->
        <ContentControl Grid.Row="1"
                        Content="{Binding SelectedTab}"
                        Margin="0">
            <ContentControl.ContentTemplate>
                <DataTemplate>
                    <!-- Content templates will be defined separately -->
                    <views:TabContentHost DataContext="{Binding}"/>
                </DataTemplate>
            </ContentControl.ContentTemplate>
        </ContentControl>

        <!-- Help Popup -->
        <views:HelpView Grid.Row="1"
                        IsVisible="{Binding IsHelpOpen}"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"/>

        <!-- Command Palette -->
        <views:CommandPaletteView Grid.Row="1"
                                  IsVisible="{Binding IsCommandPaletteOpen}"
                                  HorizontalAlignment="Center"
                                  VerticalAlignment="Top"
                                  Margin="0,100,0,0"/>

        <!-- Tab Switcher -->
        <views:TabSwitcherView Grid.Row="1"
                               IsVisible="{Binding IsTabSwitcherOpen}"
                               HorizontalAlignment="Center"
                               VerticalAlignment="Top"
                               Margin="0,100,0,0"/>
    </Grid>
</Window>
```

---

### 5.6 MainWindow.axaml.cs

**CREATE:** `src/TerminalHost/TerminalHost/MainWindow.axaml.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // Get services from DI
        _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        _configService = App.Current.Services.GetRequiredService<IConfigurationService>();
        _dialogService = App.Current.Services.GetRequiredService<IDialogService>();

        DataContext = _viewModel;

        // Restore window state
        RestoreWindowState();

        // Event handlers
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _viewModel.Initialize();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExiting)
        {
            SaveWindowState();
            _viewModel.Shutdown();
        }
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ForceClose()
    {
        _isExiting = true;
        Close();
    }

    private void RestoreWindowState()
    {
        var config = _configService.Load();
        var state = config.WindowState;

        // Validate and apply position
        var left = state.Left;
        var top = state.Top;
        var width = Math.Max(800, state.Width);
        var height = Math.Max(600, state.Height);

        // Get screen bounds
        var screens = Screens;
        if (screens.Primary != null)
        {
            var workArea = screens.Primary.WorkingArea;

            // Ensure window is on screen
            if (left < workArea.X || left > workArea.X + workArea.Width - 100)
                left = 100;
            if (top < workArea.Y || top > workArea.Y + workArea.Height - 100)
                top = 100;
        }

        Position = new PixelPoint((int)left, (int)top);
        Width = width;
        Height = height;

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        var config = _configService.Load();

        if (WindowState == WindowState.Maximized)
        {
            // Save restore bounds for maximized window
            config.WindowState.IsMaximized = true;
            // Note: Avalonia doesn't have RestoreBounds, save current position
        }
        else
        {
            config.WindowState.Left = Position.X;
            config.WindowState.Top = Position.Y;
            config.WindowState.Width = Width;
            config.WindowState.Height = Height;
            config.WindowState.IsMaximized = false;
        }

        _configService.Save(config);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Handle F1 for help
        if (e.Key == Key.F1)
        {
            _viewModel.IsHelpOpen = !_viewModel.IsHelpOpen;
            e.Handled = true;
        }

        // Handle Escape to close popups
        if (e.Key == Key.Escape)
        {
            if (_viewModel.IsHelpOpen)
            {
                _viewModel.IsHelpOpen = false;
                e.Handled = true;
            }
            else if (_viewModel.IsCommandPaletteOpen)
            {
                _viewModel.IsCommandPaletteOpen = false;
                e.Handled = true;
            }
            else if (_viewModel.IsTabSwitcherOpen)
            {
                _viewModel.IsTabSwitcherOpen = false;
                e.Handled = true;
            }
        }

        // Ctrl+Shift+P for command palette
        if (e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _viewModel.IsCommandPaletteOpen = true;
            e.Handled = true;
        }

        // Ctrl+Shift+T for tab switcher
        if (e.Key == Key.T && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _viewModel.IsTabSwitcherOpen = true;
            e.Handled = true;
        }

        // Number keys for tab selection
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            var tabIndex = e.Key switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                Key.D4 => 3,
                Key.D5 => 4,
                Key.D6 => 5,
                Key.D7 => 6,
                Key.D8 => 7,
                Key.D9 => 8,
                _ => -1
            };

            if (tabIndex >= 0 && tabIndex < _viewModel.Tabs.Count)
            {
                _viewModel.SelectedTab = _viewModel.Tabs[tabIndex];
                e.Handled = true;
            }
        }
    }
}
```

---

### 5.7 Converters Migration

**CREATE:** `src/TerminalHost/TerminalHost/Resources/Converters.axaml`

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:conv="using:TerminalHost.Converters">

    <conv:InverseBoolConverter x:Key="InverseBool"/>
    <conv:NullToBoolConverter x:Key="NullToBool"/>
    <conv:CountToVisibilityConverter x:Key="CountToVisibility"/>
    <conv:PathToFolderNameConverter x:Key="PathToFolderName"/>
    <conv:HexToBrushConverter x:Key="HexToBrush"/>
    <conv:RunStateToIconConverter x:Key="RunStateToIcon"/>
    <conv:RunStateToColorConverter x:Key="RunStateToColor"/>

</ResourceDictionary>
```

**REWRITE:** `src/TerminalHost/TerminalHost/Converters.cs`

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TerminalHost.Domain;

namespace TerminalHost.Converters;

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }
}

/// <summary>
/// Converts null to false, non-null to true.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString() == "Invert";
        var result = value != null;
        return invert ? !result : result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts count > 0 to true.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int count && count > 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Extracts folder name from full path.
/// </summary>
public class PathToFolderNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts hex color string to brush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to icon character.
/// </summary>
public class RunStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            RunState.Stopped => "▶",
            RunState.Starting => "⏳",
            RunState.Running => "⏹",
            _ => "▶"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts RunState to color brush.
/// </summary>
public class RunStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush StoppedBrush = new(Color.Parse("#808080"));
    private static readonly SolidColorBrush StartingBrush = new(Color.Parse("#CE9178"));
    private static readonly SolidColorBrush RunningBrush = new(Color.Parse("#4EC9B0"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            RunState.Stopped => StoppedBrush,
            RunState.Starting => StartingBrush,
            RunState.Running => RunningBrush,
            _ => StoppedBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

---

## 5.8 Remove P/Invoke from App.xaml.cs (Gap Fix - CRITICAL)

The current `App.xaml.cs` contains P/Invoke declarations for popup focus handling that must be completely removed.

### 5.8.1 P/Invoke Declarations to DELETE

**DELETE lines 414-425:**
```csharp
// DELETE THIS ENTIRE SECTION:
private static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    internal static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
```

### 5.8.2 WindowInteropHelper Usage to DELETE

**DELETE all HwndSource/WindowInteropHelper code (lines 274, 318, 338, 345):**
```csharp
// DELETE:
using System.Windows.Interop;

// DELETE anywhere using:
var hwndSource = PresentationSource.FromVisual(window) as HwndSource;
var helper = new WindowInteropHelper(window);
NativeMethods.SetFocus(hwndSource.Handle);
```

### 5.8.3 Popup Focus Fix Region to DELETE

**DELETE entire region (lines 252-410):**

The `#region Popup Focus Fix` section contains Windows-specific workarounds for WPF popup focus issues. Avalonia handles popups differently and doesn't need these workarounds.

```csharp
// DELETE ENTIRE REGION:
#region Popup Focus Fix
// ... all code in this region (~150 lines) ...
#endregion
```

### 5.8.4 Avalonia Popup Focus Alternative

Avalonia popups work differently. If focus management is needed:

```csharp
// Avalonia approach for popup focus
private void OnPopupOpened(object? sender, EventArgs e)
{
    if (sender is Popup popup && popup.Child is Control control)
    {
        // Focus the first focusable child
        control.Focus();
    }
}

// For bringing window to front (replaces SetForegroundWindow)
public void BringToFront()
{
    if (this.WindowState == WindowState.Minimized)
        this.WindowState = WindowState.Normal;

    this.Activate();
    this.Topmost = true;
    this.Topmost = false;
}
```

### 5.8.5 Complete List of WPF Interop to Remove from App.xaml.cs

| Line(s) | Code | Replacement |
|---------|------|-------------|
| 3-6 | `using System.Windows.*` namespaces | Avalonia equivalents |
| 232 | `DispatcherUnhandledExceptionEventArgs` | Avalonia exception handling |
| 274 | `WindowInteropHelper` | Delete |
| 306, 329 | `DispatcherPriority.Input/Background` | `Dispatcher.UIThread.Post()` |
| 318, 338, 345 | `HwndSource`, `PresentationSource` | Delete |
| 414-425 | All P/Invoke declarations | Delete |

---

## 5.9 Remove P/Invoke from MainWindow.xaml.cs (Gap Fix)

MainWindow.xaml.cs also has Windows-specific code that needs removal:

### 5.9.1 SystemParameters Usage to Replace

**Lines 285-288:**
```csharp
// BEFORE:
var virtualLeft = SystemParameters.VirtualScreenLeft;
var virtualTop = SystemParameters.VirtualScreenTop;
var virtualWidth = SystemParameters.VirtualScreenWidth;
var virtualHeight = SystemParameters.VirtualScreenHeight;

// AFTER (using IScreenService):
var screen = _screenService.GetPrimaryWorkingArea();
var virtualLeft = screen.X;
var virtualTop = screen.Y;
var virtualWidth = screen.Width;
var virtualHeight = screen.Height;
```

### 5.9.2 VisualTreeHelper Usage

**Line 1072:**
```csharp
// BEFORE:
var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);

// AFTER (Avalonia has equivalent):
var parent = element.Parent;
// Or for visual tree:
var parent = element.GetVisualParent();
```

### 5.9.3 Type Checks for WPF Controls

**Lines 1054-1057:**
```csharp
// BEFORE:
if (focused is System.Windows.Controls.TextBox ||
    focused is System.Windows.Controls.ComboBox ||
    focused is System.Windows.Controls.ListBox)

// AFTER:
if (focused is Avalonia.Controls.TextBox ||
    focused is Avalonia.Controls.ComboBox ||
    focused is Avalonia.Controls.ListBox)
```

---

## File Change Summary

| Action | File | Status | Notes |
|--------|------|--------|-------|
| **RENAMED** | `App.xaml` → `App.xaml.wpf.bak` | ✅ Done | Preserved for reference |
| **RENAMED** | `MainWindow.xaml` → `MainWindow.xaml.wpf.bak` | ✅ Done | Preserved for reference |
| **CREATE** | `App.axaml` | ✅ Done | Avalonia app with ResourceInclude |
| **CREATE** | `App.axaml.cs` | ✅ Done | Simplified DI setup |
| **CREATE** | `MainWindow.axaml` | ✅ Done | Placeholder with terminal support |
| **CREATE** | `MainWindow.axaml.cs` | ✅ Done | Terminal creation, keyboard handlers |
| **CREATE** | `Styles/Colors.axaml` | ✅ Done | 40+ colors and brushes |
| **CREATE** | `Styles/Typography.axaml` | ✅ Done | macOS fonts, sizes, weights |
| **CREATE** | `Resources/Converters.axaml` | ✅ Done | Converter resource definitions |
| **CREATE** | `Converters/Converters.cs` | ✅ Done | 20+ Avalonia converters |
| **DEFERRED** | `Styles/Controls.axaml` | ⏳ Stage 7 | Control styles |
| **DEFERRED** | `Styles/Buttons.axaml` | ⏳ Stage 7 | Button styles |
| **DEFERRED** | `Styles/ScrollBars.axaml` | ⏳ Stage 7 | ScrollBar styles |
| **UPDATED** | `TerminalHost.csproj` | ✅ Done | Added Stage 5 includes, RunState.cs |

---

## Verification Steps

| Step | Status | Notes |
|------|--------|-------|
| Application launches without errors | ✅ Pass | Build succeeded with 0 warnings, 0 errors |
| Main window displays with correct theme | ✅ Pass | Dark theme, colors apply |
| Keyboard shortcuts respond | ⏳ Partial | F1/Escape work, others need ViewModels |
| Tab selection works | ⏳ Pending | Needs Stage 7 |
| DI resolves all services | ✅ Pass | Terminal creation works |
| No P/Invoke warnings | ✅ Pass | Fresh Avalonia code, no P/Invoke |
| Terminal can be created | ✅ Pass | "New Terminal" button works |

---

## Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Next Stage

After completing Stage 5, proceed to **Stage 6: ViewModels Platform Independence** which updates ViewModels to use the new service abstractions.

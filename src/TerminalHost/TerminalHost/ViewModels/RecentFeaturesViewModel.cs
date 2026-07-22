using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the "What's New" / Recent Features page.
/// Shows palette commands grouped by the week they were introduced.
/// Used as the empty-state view and as a center panel (Ctrl+F1).
/// </summary>
public partial class RecentFeaturesViewModel : BasePanelViewModel, IPanelPlacement, IPanelOpenContext
{
    public PanelZone PreferredZone => PanelZone.Center;

    public Task OnOpenedAsync(object? context)
    {
        OnOpened();
        return Task.CompletedTask;
    }

    private readonly MainViewModel _mainViewModel;
    private readonly IConfigurationService _configService;
    private readonly ITimerService _timerService;
    private IAppTimer? _markViewedTimer;

    #region IPanelableViewModel Implementation

    public override string PanelId => "recentFeatures";
    public override string PanelTitle => "What's New";
    public override string PanelIcon => "\u2728"; // ✨
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #endregion

    [ObservableProperty]
    private ObservableCollection<FeatureWeekGroup> _weekGroups = [];

    [ObservableProperty]
    private int _unseenCount;

    public bool HasUnseenFeatures => UnseenCount > 0;

    public RecentFeaturesViewModel(MainViewModel mainViewModel, IConfigurationService configService, ITimerService timerService)
    {
        _mainViewModel = mainViewModel;
        _configService = configService;
        _timerService = timerService;
    }

    /// <summary>
    /// Loads features from the palette commands and groups them by week.
    /// </summary>
    public void LoadFeatures()
    {
        var config = _configService.Load();
        DateOnly? lastViewed = null;
        if (!string.IsNullOrEmpty(config.Settings.RecentFeaturesLastViewedDate) &&
            DateOnly.TryParse(config.Settings.RecentFeaturesLastViewedDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            lastViewed = parsed;
        }

        var commands = _mainViewModel.GetPaletteCommandsForFeatures();
        var datedCommands = commands
            .Where(c => c.IntroducedOn.HasValue)
            .OrderByDescending(c => c.IntroducedOn!.Value)
            .ThenBy(c => c.Name)
            .ToList();

        var groups = datedCommands
            .GroupBy(c =>
            {
                var date = c.IntroducedOn!.Value;
                // Get ISO week start (Monday)
                var isoYear = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));
                var isoWeek = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
                var weekStart = DateOnly.FromDateTime(ISOWeek.ToDateTime(isoYear, isoWeek, DayOfWeek.Monday));
                return weekStart;
            })
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var weekStart = g.Key;
                var weekEnd = weekStart.AddDays(6);
                var label = $"Week of {weekStart:MMM d} – {weekEnd:MMM d, yyyy}";

                var features = g
                    .OrderBy(c => c.Name)
                    .Select(c =>
                    {
                        var isNew = lastViewed == null || c.IntroducedOn!.Value > lastViewed.Value;
                        return new FeatureEntry(c, isNew);
                    })
                    .ToList();

                return new FeatureWeekGroup(label, weekStart, features);
            })
            .ToList();

        WeekGroups = new ObservableCollection<FeatureWeekGroup>(groups);
        UnseenCount = groups.SelectMany(g => g.Features).Count(f => f.IsNew);
        OnPropertyChanged(nameof(HasUnseenFeatures));
    }

    /// <summary>
    /// Called when the view is opened. Loads features and starts a timer to mark as viewed.
    /// </summary>
    public void OnOpened()
    {
        LoadFeatures();

        // Start a 3-second timer to update the last-viewed date
        _markViewedTimer?.Stop();
        _markViewedTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(3), MarkAsViewed);
        _markViewedTimer.Start();
    }

    private void MarkAsViewed()
    {
        _markViewedTimer?.Stop();
        _markViewedTimer = null;

        var config = _configService.Load();
        config.Settings.RecentFeaturesLastViewedDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _configService.Save(config);

        // Clear unseen state
        UnseenCount = 0;
        OnPropertyChanged(nameof(HasUnseenFeatures));
    }

    [RelayCommand]
    private void ExecuteFeature(PaletteCommand? command)
    {
        if (command == null) return;

        // Check if the command can execute (some require a tab to be selected)
        if (command.CanExecute != null && !command.CanExecute())
            return;

        command.Execute();
    }
}

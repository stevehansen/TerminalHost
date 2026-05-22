using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Production <see cref="IPanelPersistence"/> adapter that projects panel layout snapshots
/// onto persisted app + directory settings via <see cref="IConfigurationService"/>.
/// </summary>
/// <remarks>
/// Phase 1 scope: persists <see cref="PanelScope.AppShell"/> snapshots to a flat field on
/// <see cref="AppSettings"/>. Per-tab scope persistence is stubbed — Phase 2 will project to/from
/// the matching <see cref="DirectorySettings"/> entry once the tabId → working-directory mapping
/// is wired through the router.
/// Popup-zone entries are filtered out of every save: popups are transient and never restored.
/// </remarks>
public sealed class DirectorySettingsPanelPersistence : IPanelPersistence
{
    private readonly IConfigurationService _configService;

    public DirectorySettingsPanelPersistence(IConfigurationService configService)
    {
        _configService = configService;
    }

    public PanelLayoutSnapshot Load(PanelScope scope)
    {
        if (scope.TabId is not null)
            return new PanelLayoutSnapshot(Array.Empty<PanelLayoutEntry>());

        var config = _configService.Load();
        var stored = config.Settings.AppShellPanels;
        if (stored is null || stored.Count == 0)
            return new PanelLayoutSnapshot(Array.Empty<PanelLayoutEntry>());

        var entries = new List<PanelLayoutEntry>(stored.Count);
        foreach (var entry in stored)
        {
            if (!Enum.TryParse<PanelZone>(entry.Zone, out var zone)) continue;
            if (zone == PanelZone.Popup) continue;
            entries.Add(new PanelLayoutEntry(entry.PanelId, zone, PanelScope.AppShell, entry.IsOpen));
        }
        return new PanelLayoutSnapshot(entries);
    }

    public void Save(PanelScope scope, PanelLayoutSnapshot snapshot)
    {
        if (scope.TabId is not null) return;

        var config = _configService.Load();
        // Popups are transient — never persist them across restarts.
        var persisted = snapshot.Entries
            .Where(e => e.Zone != PanelZone.Popup)
            .Select(e => new PersistedPanelEntry
            {
                PanelId = e.PanelId,
                Zone = e.Zone.ToString(),
                IsOpen = e.IsOpen,
            })
            .ToList();

        config.Settings.AppShellPanels = persisted;
        _configService.Save(config);
    }
}

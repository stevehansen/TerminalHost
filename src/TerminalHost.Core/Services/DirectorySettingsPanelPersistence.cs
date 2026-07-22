using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Production <see cref="IPanelPersistence"/> adapter that projects panel layout snapshots
/// onto persisted app + directory settings via <see cref="IConfigurationService"/>.
/// </summary>
/// <remarks>
/// <para><see cref="PanelScope.AppShell"/> snapshots persist to a flat field on
/// <see cref="AppSettings"/>; tab-scoped snapshots persist to the matching
/// <see cref="DirectorySettings"/> entry (keyed by the scope's <c>TabId</c>, which is the
/// canonical normalized-and-lowercased working directory built by
/// <c>TabPanelScope.ForTab</c>).</para>
/// <para>Popup-zone entries are filtered out of every save: popups are transient and never
/// restored. Tab-scope snapshots round-trip RightDock entries via <c>OpenRightPanels</c> +
/// <c>ActiveRightPanel</c> and the (single) Center entry via <c>ActiveCenterPanel</c>.</para>
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
        var config = _configService.Load();

        if (scope.TabId is not null)
            return LoadTabScope(config, scope);

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
        var config = _configService.Load();

        if (scope.TabId is not null)
        {
            SaveTabScope(config, scope, snapshot);
            _configService.Save(config);
            return;
        }

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

    private static PanelLayoutSnapshot LoadTabScope(AppConfiguration config, PanelScope scope)
    {
        var key = scope.TabId!;
        if (!config.DirectorySettings.TryGetValue(key, out var dir))
            return new PanelLayoutSnapshot(Array.Empty<PanelLayoutEntry>());

        var entries = new List<PanelLayoutEntry>();

        var open = dir.OpenRightPanels;
        if (open is not null && open.Count > 0)
        {
            var active = dir.ActiveRightPanel;
            foreach (var panelId in open)
            {
                entries.Add(new PanelLayoutEntry(
                    panelId,
                    PanelZone.RightDock,
                    scope,
                    IsOpen: true,
                    IsActive: active is not null && string.Equals(active, panelId, StringComparison.Ordinal)));
            }
        }

        if (!string.IsNullOrEmpty(dir.ActiveCenterPanel))
        {
            entries.Add(new PanelLayoutEntry(
                dir.ActiveCenterPanel,
                PanelZone.Center,
                scope,
                IsOpen: true,
                IsActive: true));
        }

        return new PanelLayoutSnapshot(entries);
    }

    private static void SaveTabScope(AppConfiguration config, PanelScope scope, PanelLayoutSnapshot snapshot)
    {
        var key = scope.TabId!;
        if (!config.DirectorySettings.TryGetValue(key, out var dir))
        {
            dir = new DirectorySettings();
            config.DirectorySettings[key] = dir;
        }

        var rightDockEntries = snapshot.Entries
            .Where(e => e.Zone == PanelZone.RightDock)
            .ToList();

        dir.OpenRightPanels = rightDockEntries.Select(e => e.PanelId).ToList();
        dir.ActiveRightPanel = rightDockEntries.FirstOrDefault(e => e.IsActive)?.PanelId;

        // Center surface is single-slot; round-trip the (at most one) Center entry per tab.
        var centerEntry = snapshot.Entries.FirstOrDefault(e => e.Zone == PanelZone.Center);
        dir.ActiveCenterPanel = centerEntry?.PanelId;
    }
}

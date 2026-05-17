using System;
using System.Collections.Generic;
using System.Diagnostics;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class TabRestoreCoordinator : ITabRestoreCoordinator
{
    private List<CenterPanelRestoreEventArgs>? _pending;

    public bool IsBatching => _pending != null;

    public event EventHandler<CenterPanelRestoreEventArgs>? RestoreRequested;

    public void Request(CenterPanelRestoreEventArgs args)
    {
        if (_pending != null)
        {
            _pending.Add(args);
        }
        else
        {
            RestoreRequested?.Invoke(this, args);
        }
    }

    public void BeginBatch()
    {
        if (_pending != null)
        {
            Debug.WriteLine("TabRestoreCoordinator.BeginBatch called while already batching; reusing existing queue.");
            return;
        }
        _pending = new List<CenterPanelRestoreEventArgs>();
    }

    public void EndBatch(object? selectedTab)
    {
        var pending = _pending;
        _pending = null;
        if (pending == null) return;

        CenterPanelRestoreEventArgs? selectedArgs = null;
        foreach (var args in pending)
        {
            if (selectedTab != null && ReferenceEquals(args.Tab, selectedTab) && selectedArgs == null)
            {
                // Capture the first match so we dispatch it last with its original SkipDataLoad value.
                selectedArgs = args;
                continue;
            }
            // Isolate handler failures: one bad restore must not strand the other 59 tabs.
            try
            {
                RestoreRequested?.Invoke(this, new CenterPanelRestoreEventArgs
                {
                    Tab = args.Tab,
                    PanelId = args.PanelId,
                    GitPanelActiveTab = args.GitPanelActiveTab,
                    SkipDataLoad = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TabRestoreCoordinator: RestoreRequested handler threw for tab restore: {ex}");
            }
        }

        if (selectedArgs != null)
        {
            try
            {
                RestoreRequested?.Invoke(this, selectedArgs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TabRestoreCoordinator: RestoreRequested handler threw for selected tab restore: {ex}");
            }
        }
    }
}

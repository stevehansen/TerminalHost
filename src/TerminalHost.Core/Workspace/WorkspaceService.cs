using System.Collections.ObjectModel;
using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Default <see cref="IWorkspaceService"/> implementation. Owns the tab
/// collection + selection state and provides the host-agnostic
/// mutation/query operations.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private ITabViewModel? _selectedTab;

    public ObservableCollection<ITabViewModel> Tabs { get; } = [];

    public ITabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value)) return;
            var old = _selectedTab;
            if (old is not null) old.IsSelected = false;
            _selectedTab = value;
            if (value is not null) value.IsSelected = true;
            SelectedTabChanged?.Invoke(this, new TabSelectionChangedEventArgs(old, value));
        }
    }

    public event EventHandler<TabSelectionChangedEventArgs>? SelectedTabChanged;

    public void Move(int oldIndex, int newIndex)
    {
        var count = Tabs.Count;
        if (count <= 1) return;
        if (oldIndex < 0 || oldIndex >= count) return;
        var clamped = Math.Clamp(newIndex, 0, count - 1);
        if (clamped == oldIndex) return;
        Tabs.Move(oldIndex, clamped);
    }

    public void MoveToFront(ITabViewModel? tab)
    {
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        if (index > 0) Tabs.Move(index, 0);
    }

    public void MoveToEnd(ITabViewModel? tab)
    {
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        if (index >= 0 && index < Tabs.Count - 1)
            Tabs.Move(index, Tabs.Count - 1);
    }

    public void RemoveAndPickNext(ITabViewModel? tab)
    {
        if (tab is null) return;
        if (!Tabs.Remove(tab)) return;
        if (ReferenceEquals(_selectedTab, tab))
        {
            SelectedTab = Tabs.Count > 0 ? Tabs[^1] : null;
        }
    }

    public IEnumerable<T> FindByWorkingDirectory<T>(string? workingDirectory) where T : class, ITabViewModel
    {
        if (string.IsNullOrEmpty(workingDirectory)) return [];
        var normalized = NormalizeWorkingDirectory(workingDirectory);
        if (normalized.Length == 0) return [];
        return Tabs.OfType<T>().Where(t =>
            t.WorkingDirectory.Length > 0 &&
            string.Equals(NormalizeWorkingDirectory(t.WorkingDirectory), normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Canonicalizes a working-directory path for tab-identity comparison:
    /// applies <see cref="Path.GetFullPath(string)"/> and strips trailing
    /// directory separators. Invalid input (null/empty, paths that throw
    /// inside <c>GetFullPath</c>) collapses to an empty string so callers
    /// can compare without try/catch — sentinel values like "Settings" or
    /// "Profiles" used by non-project tab view models also normalize to
    /// themselves (no slashes to trim) and won't match a real path.
    /// </summary>
    public static string NormalizeWorkingDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return string.Empty;
        try
        {
            return Path.GetFullPath(workingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    public IReadOnlyList<ITabViewModel> GetTabsToCloseExcept(ITabViewModel? keep)
    {
        if (keep is null) return [];
        return Tabs.Where(t => t != keep && t.IsCloseable).ToList();
    }

    public IReadOnlyList<ITabViewModel> GetTabsToCloseToRightOf(ITabViewModel? tab)
    {
        if (tab is null) return [];
        var index = Tabs.IndexOf(tab);
        if (index < 0) return [];
        return Tabs.Skip(index + 1).Where(t => t.IsCloseable).ToList();
    }
}

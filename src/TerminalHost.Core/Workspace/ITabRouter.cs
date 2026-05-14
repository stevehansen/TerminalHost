using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Routes singleton tab opens (Settings, Dashboard, Statistics, Timeline, etc.)
/// through a single dedupe + activation point so MainViewModel no longer
/// hand-rolls "find existing or create new" for each tab type.
/// </summary>
public interface ITabRouter
{
    /// <summary>
    /// Focuses an existing tab of type <typeparamref name="T"/> if one exists,
    /// otherwise constructs one via the registered factory, runs the registered
    /// onCreated callback, appends it to the tab collection, and selects it.
    /// </summary>
    T OpenSingleton<T>() where T : class, ITabViewModel;

    /// <summary>
    /// Same dedupe-or-create flow as the parameterless overload, but invokes
    /// <paramref name="configure"/> on the returned tab regardless of whether
    /// it already existed. On the create path, configure runs after the
    /// registered onCreated callback so callers can override default state.
    /// </summary>
    T OpenSingleton<T>(Action<T> configure) where T : class, ITabViewModel;

    /// <summary>
    /// Removes the first tab of type <typeparamref name="T"/> if present. No-op otherwise.
    /// Does not dispose the tab.
    /// </summary>
    void Close<T>() where T : class, ITabViewModel;

    /// <summary>
    /// Whether at least one tab of type <typeparamref name="T"/> is currently open.
    /// </summary>
    bool IsOpen<T>() where T : class, ITabViewModel;
}

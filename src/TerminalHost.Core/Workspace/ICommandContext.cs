using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Read-only snapshot of MainViewModel state passed to each
/// <see cref="ICommandProvider"/>. Today providers mostly close over their own
/// MainViewModel reference and ignore the context; the seam exists so future
/// per-feature providers can be constructed without a MainViewModel handle.
/// </summary>
public interface ICommandContext
{
    ITabViewModel? ActiveTab { get; }
    bool HasService<T>() where T : class;
}

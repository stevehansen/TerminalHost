namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Opt-in sibling interface for panelable view models that need to react after being
/// mounted by the router. Implementations should be idempotent — the router may invoke
/// this on user-driven Show and Move-completion, but not during Restore replays.
/// </summary>
public interface IPanelOpenContext
{
    Task OnOpenedAsync(object? context);
}

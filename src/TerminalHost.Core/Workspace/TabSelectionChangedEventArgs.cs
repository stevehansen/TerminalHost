using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Carries the old/new selection across <see cref="IWorkspaceService.SelectedTabChanged"/>.
/// </summary>
public sealed class TabSelectionChangedEventArgs(ITabViewModel? oldValue, ITabViewModel? newValue) : EventArgs
{
    public ITabViewModel? OldValue { get; } = oldValue;
    public ITabViewModel? NewValue { get; } = newValue;
}

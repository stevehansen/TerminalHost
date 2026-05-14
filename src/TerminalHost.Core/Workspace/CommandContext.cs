using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

public sealed class CommandContext : ICommandContext
{
    private readonly Func<ITabViewModel?> _activeTab;
    private readonly Func<Type, object?> _serviceLocator;

    public CommandContext(Func<ITabViewModel?> activeTab, Func<Type, object?> serviceLocator)
    {
        _activeTab = activeTab ?? throw new ArgumentNullException(nameof(activeTab));
        _serviceLocator = serviceLocator ?? throw new ArgumentNullException(nameof(serviceLocator));
    }

    public ITabViewModel? ActiveTab => _activeTab();

    public bool HasService<T>() where T : class => _serviceLocator(typeof(T)) != null;
}

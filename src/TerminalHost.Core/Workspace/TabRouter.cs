using System.Collections.ObjectModel;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

public sealed class TabRouter : ITabRouter
{
    private readonly ObservableCollection<ITabViewModel> _tabs;
    private readonly Action<ITabViewModel> _setSelected;
    private readonly Dictionary<Type, Registration> _registrations = new();

    public TabRouter(ObservableCollection<ITabViewModel> tabs, Action<ITabViewModel> setSelected)
    {
        _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
        _setSelected = setSelected ?? throw new ArgumentNullException(nameof(setSelected));
    }

    public void Register<T>(Func<T> factory, Action<T>? onCreated = null) where T : class, ITabViewModel
    {
        ArgumentNullException.ThrowIfNull(factory);
        _registrations[typeof(T)] = new Registration(
            () => factory(),
            tab => onCreated?.Invoke((T)tab));
    }

    public T OpenSingleton<T>() where T : class, ITabViewModel
    {
        return OpenInternal<T>(configure: null);
    }

    public T OpenSingleton<T>(Action<T> configure) where T : class, ITabViewModel
    {
        ArgumentNullException.ThrowIfNull(configure);
        return OpenInternal(configure);
    }

    public void Close<T>() where T : class, ITabViewModel
    {
        var existing = _tabs.OfType<T>().FirstOrDefault();
        if (existing != null)
            _tabs.Remove(existing);
    }

    public bool IsOpen<T>() where T : class, ITabViewModel
    {
        return _tabs.OfType<T>().Any();
    }

    private T OpenInternal<T>(Action<T>? configure) where T : class, ITabViewModel
    {
        var existing = _tabs.OfType<T>().FirstOrDefault();
        if (existing != null)
        {
            configure?.Invoke(existing);
            _setSelected(existing);
            return existing;
        }

        if (!_registrations.TryGetValue(typeof(T), out var registration))
            throw new InvalidOperationException(
                $"No factory registered for tab type {typeof(T).FullName}. Call TabRouter.Register<{typeof(T).Name}>(...) before OpenSingleton<{typeof(T).Name}>().");

        var tab = (T)registration.Factory();
        registration.OnCreated(tab);
        configure?.Invoke(tab);
        _tabs.Add(tab);
        _setSelected(tab);
        return tab;
    }

    private sealed record Registration(Func<ITabViewModel> Factory, Action<ITabViewModel> OnCreated);
}

using System.ComponentModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Default implementation of <see cref="IPanelRouter"/>. See the interface for the contract.
/// </summary>
public sealed class PanelRouter : IPanelRouter, IDisposable
{
    private readonly Dictionary<(PanelZone Zone, PanelScope Scope), IPanelSurface> _surfaces;
    private readonly IPanelPersistence _persistence;
    private readonly IDispatcherService _dispatcher;
    private readonly Func<Type, IPanelableViewModel?>? _viewModelFactory;

    private readonly Dictionary<string, Registration> _registry = new(StringComparer.Ordinal);
    private readonly Dictionary<IPanelableViewModel, EventHandler<PanelStateChangeRequestedEventArgs>> _vmHandlers = new();
    private readonly Dictionary<IPanelableViewModel, PropertyChangedEventHandler> _vmOpenHandlers = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<PanelRoutedEventArgs>? Routed;

    /// <summary>
    /// Creates a new router. <paramref name="surfaces"/> are indexed by their <c>(Zone, Scope)</c>
    /// pair; later registrations for the same key overwrite earlier ones.
    /// </summary>
    public PanelRouter(
        IEnumerable<IPanelSurface> surfaces,
        IPanelPersistence persistence,
        IDispatcherService dispatcher,
        Func<Type, IPanelableViewModel?>? viewModelFactory = null)
    {
        _persistence = persistence;
        _dispatcher = dispatcher;
        _viewModelFactory = viewModelFactory;
        _surfaces = new Dictionary<(PanelZone, PanelScope), IPanelSurface>();

        foreach (var surface in surfaces)
        {
            _surfaces[(surface.Zone, surface.Scope)] = surface;
            surface.DismissRequested += OnSurfaceDismissRequested;
        }
    }

    /// <inheritdoc />
    public void Show<TPanel>() where TPanel : IPanelableViewModel
    {
        ThrowIfDisposed();
        if (_viewModelFactory is null)
            throw new InvalidOperationException(
                $"Cannot Show<{typeof(TPanel).Name}>(): no view model factory was supplied to PanelRouter.");

        var vm = _viewModelFactory(typeof(TPanel))
            ?? throw new InvalidOperationException(
                $"View model factory returned null for {typeof(TPanel).FullName}.");
        Show(vm);
    }

    /// <inheritdoc />
    public void Show(IPanelableViewModel vm) => Show(vm, new PanelShowOptions());

    /// <inheritdoc />
    public void Show(IPanelableViewModel vm, PanelShowOptions options)
    {
        ThrowIfDisposed();
        var zone = ResolveZone(vm, options);
        var scope = ResolveScope(vm, options);

        ShowInternal(vm, zone, scope, options);
    }

    /// <inheritdoc />
    public void Move(string panelId, PanelZone newZone) => Move(panelId, newZone, options: null);

    /// <inheritdoc />
    public void Move(string panelId, PanelZone newZone, PanelShowOptions? options)
    {
        ThrowIfDisposed();
        Registration existing;
        string registrationKey;
        IPanelSurface oldSurface;
        IPanelSurface newSurface;
        PanelShowOptions effectiveOptions;
        lock (_lock)
        {
            var key = FindRegistrationKeyByPanelId(panelId);
            if (key is null) return;
            registrationKey = key;
            existing = _registry[key];

            if (existing.Zone == newZone)
                return;

            if (!_surfaces.TryGetValue((newZone, existing.Scope), out var ns))
                throw new InvalidOperationException(
                    $"No surface registered for zone '{newZone}' in scope '{FormatScope(existing.Scope)}'.");

            if (!_surfaces.TryGetValue((existing.Zone, existing.Scope), out var os))
                throw new InvalidOperationException(
                    $"No surface registered for zone '{existing.Zone}' in scope '{FormatScope(existing.Scope)}'.");

            newSurface = ns;
            oldSurface = os;
            effectiveOptions = options ?? existing.Options;
        }

        MoveCore(existing, registrationKey, newZone, oldSurface, newSurface, effectiveOptions);
    }

    private void MoveCore(
        Registration existing,
        string registrationKey,
        PanelZone newZone,
        IPanelSurface oldSurface,
        IPanelSurface newSurface,
        PanelShowOptions effectiveOptions)
    {
        oldSurface.Unmount(existing.Vm.PanelId);

        ApplyDisplayState(existing.Vm, newZone);

        try
        {
            newSurface.Mount(existing.Vm, BuildMountOptions(existing.Vm, effectiveOptions));
        }
        catch (Exception mountEx)
        {
            ApplyDisplayState(existing.Vm, existing.Zone);
            try
            {
                oldSurface.Mount(existing.Vm, BuildMountOptions(existing.Vm, existing.Options));
            }
            catch (Exception rollbackEx)
            {
                // Both the new-surface mount and the rollback mount failed. Force-close the panel:
                // remove the registry entry, unsubscribe handlers, mark VM closed, raise Routed(null),
                // persist, and surface both failures via AggregateException.
                lock (_lock)
                {
                    _registry.Remove(registrationKey);
                }
                UnsubscribeStateChanges(existing.Vm);
                UnsubscribeIsOpen(existing.Vm);
                existing.Vm.IsOpen = false;
                RaiseRouted(existing.Vm.PanelId, existing.Zone, newZone: null, existing.Scope);
                PersistScope(existing.Scope);
                throw new AggregateException(
                    "Panel move failed and rollback to the original surface also failed. Panel has been force-closed.",
                    mountEx, rollbackEx);
            }
            throw;
        }

        lock (_lock)
        {
            // Only rewrite if the registration is still here (defensive; tests are single-threaded).
            if (_registry.ContainsKey(registrationKey))
                _registry[registrationKey] = existing with { Zone = newZone, Options = effectiveOptions };
        }

        RaiseRouted(existing.Vm.PanelId, existing.Zone, newZone, existing.Scope);
        PersistScope(existing.Scope);
    }

    /// <inheritdoc />
    public void Close(string panelId)
    {
        ThrowIfDisposed();
        Registration existing;
        lock (_lock)
        {
            var key = FindRegistrationKeyByPanelId(panelId);
            if (key is null) return;
            existing = _registry[key];
            _registry.Remove(key);
        }

        UnsubscribeStateChanges(existing.Vm);
        UnsubscribeIsOpen(existing.Vm);

        if (_surfaces.TryGetValue((existing.Zone, existing.Scope), out var surface))
            surface.Unmount(panelId);

        existing.Vm.IsOpen = false;

        RaiseRouted(panelId, existing.Zone, newZone: null, existing.Scope);
        PersistScope(existing.Scope);
    }

    /// <inheritdoc />
    public void CloseZone(PanelZone zone, PanelScope scope)
    {
        ThrowIfDisposed();
        List<string> registrationKeys;
        lock (_lock)
        {
            registrationKeys = _registry
                .Where(kvp => kvp.Value.Zone == zone && kvp.Value.Scope == scope)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        foreach (var key in registrationKeys)
            CloseByRegistrationKey(key);
    }

    private void CloseByRegistrationKey(string registrationKey)
    {
        Registration existing;
        lock (_lock)
        {
            if (!_registry.TryGetValue(registrationKey, out var current)) return;
            existing = current;
            _registry.Remove(registrationKey);
        }

        UnsubscribeStateChanges(existing.Vm);
        UnsubscribeIsOpen(existing.Vm);

        if (_surfaces.TryGetValue((existing.Zone, existing.Scope), out var surface))
            surface.Unmount(existing.Vm.PanelId);

        existing.Vm.IsOpen = false;
        RaiseRouted(existing.Vm.PanelId, existing.Zone, newZone: null, existing.Scope);
        PersistScope(existing.Scope);
    }

    /// <inheritdoc />
    public bool IsOpen(string panelId)
    {
        lock (_lock) return _registry.Values.Any(r => r.Vm.PanelId == panelId);
    }

    /// <inheritdoc />
    public IPanelableViewModel? Get(string panelId)
    {
        lock (_lock) return _registry.Values.FirstOrDefault(r => r.Vm.PanelId == panelId)?.Vm;
    }

    /// <inheritdoc />
    public void Restore(PanelScope scope, Func<string, IPanelableViewModel?> resolveVm)
    {
        ThrowIfDisposed();
        var snapshot = _persistence.Load(scope);
        foreach (var entry in snapshot.Entries)
        {
            if (!entry.IsOpen) continue;
            var vm = resolveVm(entry.PanelId);
            if (vm is null) continue;
            Show(vm, new PanelShowOptions(Zone: entry.Zone, Scope: entry.Scope, ForceShow: true));
        }
    }

    private void ShowInternal(IPanelableViewModel vm, PanelZone zone, PanelScope scope, PanelShowOptions options)
    {
        // Step 1: decide what to do under lock (toggle/move/focus/new).
        // We don't perform the surface ops under lock — only the registry decision is atomic.
        ShowDecision decision;
        Registration? existing;
        string? newRegistrationKey = null;

        lock (_lock)
        {
            var existingKey = FindRegistrationKey(vm.PanelId, scope);
            existing = existingKey is null ? null : _registry[existingKey];

            // S2: enforce per-(panelId, scope) mode consistency.
            if (existing is not null && existing.AllowMultiInstance != options.AllowMultiInstance)
            {
                throw new InvalidOperationException(
                    $"Panel '{vm.PanelId}' in scope '{FormatScope(scope)}' was first registered with " +
                    $"AllowMultiInstance={existing.AllowMultiInstance}; cannot Show with AllowMultiInstance={options.AllowMultiInstance}.");
            }

            if (!options.AllowMultiInstance && existing is not null)
            {
                if (existing.Zone != zone)
                {
                    decision = ShowDecision.Move;
                }
                else if (options.ForceShow)
                {
                    decision = ShowDecision.Focus;
                }
                else
                {
                    decision = ShowDecision.ToggleClose;
                }
            }
            else
            {
                if (!_surfaces.ContainsKey((zone, scope)))
                    throw new InvalidOperationException(
                        $"No surface registered for zone '{zone}' in scope '{FormatScope(scope)}'.");

                // Reserve a registration key atomically with the probe.
                newRegistrationKey = options.AllowMultiInstance
                    ? BuildMultiInstanceKeyUnderLock(vm.PanelId, scope)
                    : BuildSingleInstanceKey(vm.PanelId, scope);
                _registry[newRegistrationKey] = new Registration(vm, zone, scope, options, options.AllowMultiInstance);
                decision = ShowDecision.New;
            }
        }

        // Step 2: act on the decision, outside the lock.
        switch (decision)
        {
            case ShowDecision.Move:
            {
                // Reuse Move's plumbing to keep semantics identical (display state, rollback, raise, persist).
                Move(vm.PanelId, zone);
                if (_surfaces.TryGetValue((zone, scope), out var s))
                    s.Focus(vm.PanelId);
                return;
            }
            case ShowDecision.Focus:
            {
                if (_surfaces.TryGetValue((zone, scope), out var s))
                    s.Focus(vm.PanelId);
                return;
            }
            case ShowDecision.ToggleClose:
            {
                Close(vm.PanelId);
                return;
            }
            case ShowDecision.New:
            {
                ApplyDisplayState(vm, zone);
                vm.IsOpen = true;
                SubscribeStateChanges(vm);
                SubscribeIsOpen(vm);
                var surface = _surfaces[(zone, scope)];
                try
                {
                    surface.Mount(vm, BuildMountOptions(vm, options));
                }
                catch
                {
                    // Mount failed — roll back the registry reservation so callers see a clean state.
                    lock (_lock)
                    {
                        if (newRegistrationKey is not null)
                            _registry.Remove(newRegistrationKey);
                    }
                    UnsubscribeStateChanges(vm);
                    UnsubscribeIsOpen(vm);
                    vm.IsOpen = false;
                    throw;
                }
                RaiseRouted(vm.PanelId, oldZone: null, newZone: zone, scope);
                PersistScope(scope);
                return;
            }
        }
    }

    private enum ShowDecision { New, Move, Focus, ToggleClose }

    private string BuildSingleInstanceKey(string panelId, PanelScope scope) =>
        $"{panelId}|{FormatScope(scope)}";

    // Caller must hold _lock — probe-and-reserve happens atomically with the dictionary insert
    // performed by the caller immediately after.
    private string BuildMultiInstanceKeyUnderLock(string panelId, PanelScope scope)
    {
        var prefix = $"{panelId}|{FormatScope(scope)}#";
        var n = 1;
        while (_registry.ContainsKey($"{prefix}{n}")) n++;
        return $"{prefix}{n}";
    }

    private string? FindRegistrationKey(string panelId, PanelScope scope)
    {
        foreach (var kvp in _registry)
        {
            if (kvp.Value.Vm.PanelId == panelId && kvp.Value.Scope == scope)
                return kvp.Key;
        }
        return null;
    }

    private string? FindRegistrationKeyByPanelId(string panelId)
    {
        foreach (var kvp in _registry)
        {
            if (kvp.Value.Vm.PanelId == panelId)
                return kvp.Key;
        }
        return null;
    }

    private PanelZone ResolveZone(IPanelableViewModel vm, PanelShowOptions options)
    {
        if (options.Zone.HasValue) return options.Zone.Value;
        if (vm is IPanelPlacement placement) return placement.PreferredZone;
        return PanelZone.Popup;
    }

    private PanelScope ResolveScope(IPanelableViewModel vm, PanelShowOptions options)
    {
        if (options.Scope.HasValue) return options.Scope.Value;
        if (vm is IPanelPlacement placement) return placement.PreferredScope;
        return PanelScope.AppShell;
    }

    private static PanelMountOptions BuildMountOptions(IPanelableViewModel vm, PanelShowOptions options) =>
        new(
            Size: vm.SizePreset,
            DismissOnClickOutside: false,
            AlwaysOnTop: options.AlwaysOnTop,
            ConfirmOnClose: vm is IPanelCloseGuard);

    private static void ApplyDisplayState(IPanelableViewModel vm, PanelZone zone)
    {
        vm.DisplayState = zone == PanelZone.Window ? PanelDisplayState.Window : PanelDisplayState.Panel;
        if (zone == PanelZone.LeftDock) vm.PreferredSide = PanelSide.Left;
        else if (zone == PanelZone.RightDock) vm.PreferredSide = PanelSide.Right;
        // Center/Popup/Window leave PreferredSide untouched — it only describes dock orientation.
    }

    /// <summary>Thread-safe. Subscribes the router's handler to <c>vm.StateChangeRequested</c> exactly once.</summary>
    private void SubscribeStateChanges(IPanelableViewModel vm)
    {
        EventHandler<PanelStateChangeRequestedEventArgs> handler;
        lock (_lock)
        {
            if (_vmHandlers.ContainsKey(vm)) return;
            handler = (sender, args) =>
            {
                if (sender is not IPanelableViewModel src) return;
                var targetZone = args.RequestedState == PanelDisplayState.Window
                    ? PanelZone.Window
                    : args.DockSide == PanelSide.Left ? PanelZone.LeftDock : PanelZone.RightDock;
                Move(src.PanelId, targetZone);
            };
            _vmHandlers[vm] = handler;
        }
        vm.StateChangeRequested += handler;
    }

    /// <summary>Thread-safe. Removes the router's handler from <c>vm.StateChangeRequested</c> if present.</summary>
    private void UnsubscribeStateChanges(IPanelableViewModel vm)
    {
        EventHandler<PanelStateChangeRequestedEventArgs>? handler;
        lock (_lock)
        {
            if (!_vmHandlers.TryGetValue(vm, out handler)) return;
            _vmHandlers.Remove(vm);
        }
        vm.StateChangeRequested -= handler;
    }

    /// <summary>
    /// Subscribes to <c>vm.PropertyChanged</c> so externally-driven <c>IsOpen=false</c> (× button,
    /// <c>BasePanelViewModel.CloseCommand</c>, custom subclass logic) routes back through <c>Close</c>.
    /// Router-initiated <c>Close</c> removes the registry entry before flipping <c>IsOpen</c>, so the
    /// handler probes the registry to avoid double-close.
    /// </summary>
    private void SubscribeIsOpen(IPanelableViewModel vm)
    {
        PropertyChangedEventHandler handler;
        lock (_lock)
        {
            if (_vmOpenHandlers.ContainsKey(vm)) return;
            handler = (sender, args) =>
            {
                if (args.PropertyName != nameof(IPanelableViewModel.IsOpen)) return;
                if (sender is not IPanelableViewModel src || src.IsOpen) return;
                bool stillRegistered;
                lock (_lock)
                {
                    stillRegistered = FindRegistrationKeyByPanelId(src.PanelId) is not null;
                }
                if (!stillRegistered) return;
                if (_dispatcher.CheckAccess()) Close(src.PanelId);
                else _dispatcher.BeginInvoke(() => Close(src.PanelId));
            };
            _vmOpenHandlers[vm] = handler;
        }
        vm.PropertyChanged += handler;
    }

    private void UnsubscribeIsOpen(IPanelableViewModel vm)
    {
        PropertyChangedEventHandler? handler;
        lock (_lock)
        {
            if (!_vmOpenHandlers.TryGetValue(vm, out handler)) return;
            _vmOpenHandlers.Remove(vm);
        }
        vm.PropertyChanged -= handler;
    }

    // The lock around _vmHandlers (B1) is what prevents corruption if a misconfigured dispatcher
    // returns CheckAccess() == true from a background thread and re-enters via Close.
    private void OnSurfaceDismissRequested(object? sender, PanelDismissEventArgs e)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess())
            Close(e.PanelId);
        else
            _dispatcher.BeginInvoke(() => Close(e.PanelId));
    }

    private void RaiseRouted(string panelId, PanelZone? oldZone, PanelZone? newZone, PanelScope scope) =>
        Routed?.Invoke(this, new PanelRoutedEventArgs(panelId, oldZone, newZone, scope));

    private void PersistScope(PanelScope scope)
    {
        List<PanelLayoutEntry> entries;
        lock (_lock)
        {
            entries = _registry.Values
                .Where(r => r.Scope == scope)
                .Select(r => new PanelLayoutEntry(r.Vm.PanelId, r.Zone, r.Scope, IsOpen: true))
                .ToList();
        }
        _persistence.Save(scope, new PanelLayoutSnapshot(entries));
    }

    private static string FormatScope(PanelScope scope) => scope.TabId is null ? "AppShell" : $"Tab:{scope.TabId}";

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PanelRouter));
    }

    /// <summary>
    /// Releases all event subscriptions held by the router. After disposal, public methods
    /// throw <see cref="ObjectDisposedException"/> and surface dismiss events are ignored.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var surface in _surfaces.Values)
            surface.DismissRequested -= OnSurfaceDismissRequested;

        List<KeyValuePair<IPanelableViewModel, EventHandler<PanelStateChangeRequestedEventArgs>>> handlers;
        List<KeyValuePair<IPanelableViewModel, PropertyChangedEventHandler>> openHandlers;
        lock (_lock)
        {
            handlers = _vmHandlers.ToList();
            _vmHandlers.Clear();
            openHandlers = _vmOpenHandlers.ToList();
            _vmOpenHandlers.Clear();
        }
        foreach (var kvp in handlers)
            kvp.Key.StateChangeRequested -= kvp.Value;
        foreach (var kvp in openHandlers)
            kvp.Key.PropertyChanged -= kvp.Value;
    }

    private sealed record Registration(
        IPanelableViewModel Vm,
        PanelZone Zone,
        PanelScope Scope,
        PanelShowOptions Options,
        bool AllowMultiInstance);
}

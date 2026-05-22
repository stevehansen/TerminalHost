using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// In-memory <see cref="IPanelPersistence"/> adapter. Backed by a dictionary keyed by scope.
/// </summary>
public sealed class InMemoryPanelPersistence : IPanelPersistence
{
    private readonly Dictionary<PanelScope, PanelLayoutSnapshot> _store = new();

    public int SaveCallCount { get; private set; }

    public PanelLayoutSnapshot Load(PanelScope scope) =>
        _store.TryGetValue(scope, out var snap) ? snap : new PanelLayoutSnapshot(Array.Empty<PanelLayoutEntry>());

    public void Save(PanelScope scope, PanelLayoutSnapshot snapshot)
    {
        _store[scope] = snapshot;
        SaveCallCount++;
    }

    /// <summary>Test helper: seed a snapshot for a scope without invoking <see cref="Save"/>.</summary>
    public void Seed(PanelScope scope, PanelLayoutSnapshot snapshot) => _store[scope] = snapshot;
}

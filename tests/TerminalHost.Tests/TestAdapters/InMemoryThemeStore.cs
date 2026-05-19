using TerminalHost.Core.Interfaces.Spark;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>In-memory <see cref="IThemeStore"/>. Last-written-wins.</summary>
public sealed class InMemoryThemeStore : IThemeStore
{
    private string _theme;

    public InMemoryThemeStore(string initial = "holographic")
    {
        _theme = initial;
    }

    public string Current => _theme;

    public string Load() => _theme;

    public void Save(string theme) => _theme = theme;
}

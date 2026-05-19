using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Interfaces.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Production <see cref="IThemeStore"/> over <see cref="IConfigurationService"/>.
/// Touches only <c>Settings.Timeline.SparkTheme</c>.
/// </summary>
public sealed class ConfigThemeStore : IThemeStore
{
    private readonly IConfigurationService _config;
    private readonly object _gate = new();

    public ConfigThemeStore(IConfigurationService config)
    {
        _config = config;
    }

    public string Load()
    {
        var cfg = _config.Load();
        var theme = cfg.Settings.Timeline.SparkTheme;
        return string.IsNullOrEmpty(theme) ? "holographic" : theme;
    }

    public void Save(string theme)
    {
        if (string.IsNullOrEmpty(theme)) return;
        // S6: make the load/modify/save sequence instance-safe. The underlying
        // IConfigurationService is the same pattern as the rest of the codebase
        // (no cross-instance write coordination); the lock here at least
        // prevents two concurrent Save calls on the same store from racing.
        lock (_gate)
        {
            var cfg = _config.Load();
            if (cfg.Settings.Timeline.SparkTheme == theme) return;
            cfg.Settings.Timeline.SparkTheme = theme;
            _config.Save(cfg);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Decorator that wraps a platform-specific <see cref="ISystemInfoService"/>
/// and overrides font enumeration with Avalonia's <see cref="FontManager"/>,
/// which knows exactly which fonts the UI framework can render.
/// </summary>
internal sealed class AvaloniaSystemInfoDecorator : ISystemInfoService
{
    private readonly ISystemInfoService _inner;

    public AvaloniaSystemInfoDecorator(ISystemInfoService inner)
    {
        _inner = inner;
    }

    public string GetApplicationDataPath() => _inner.GetApplicationDataPath();
    public string GetUserHomePath() => _inner.GetUserHomePath();
    public string GetTempPath() => _inner.GetTempPath();
    public string GetDefaultShell() => _inner.GetDefaultShell();
    public string GetDefaultCustomCommand() => _inner.GetDefaultCustomCommand();

    public IEnumerable<string> GetInstalledFontFamilies()
        => FontManager.Current.SystemFonts.Select(f => f.Name);

    public bool IsFontInstalled(string fontFamilyName)
        => FontManager.Current.SystemFonts
            .Any(f => f.Name.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));
}

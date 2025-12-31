using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            return await clipboard.GetTextAsync();
        }
        return null;
    }

    public async Task<bool> ContainsTextAsync()
    {
        var text = await GetTextAsync();
        return !string.IsNullOrEmpty(text);
    }

    public async Task ClearAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.ClearAsync();
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }
}

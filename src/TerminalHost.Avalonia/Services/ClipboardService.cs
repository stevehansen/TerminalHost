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
#pragma warning disable CS0618 // IClipboard.GetTextAsync is obsolete in newer Avalonia
            return await clipboard.GetTextAsync();
#pragma warning restore CS0618
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

    public Task<bool> ContainsImageAsync()
    {
        // Avalonia's clipboard API doesn't expose image directly — not supported on macOS yet
        return Task.FromResult(false);
    }

    public Task<byte[]?> GetImagePngAsync()
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task SetImagePngAsync(byte[] pngData)
    {
        return Task.CompletedTask;
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

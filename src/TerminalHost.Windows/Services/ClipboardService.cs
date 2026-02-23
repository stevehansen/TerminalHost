using TerminalHost.Core.Interfaces;
using WpfClipboard = System.Windows.Clipboard;

namespace TerminalHost.Windows.Services;

public class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text)
    {
        WpfClipboard.SetText(text);
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync()
    {
        return Task.FromResult(WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null);
    }

    public Task<bool> ContainsTextAsync()
    {
        return Task.FromResult(WpfClipboard.ContainsText());
    }

    public Task ClearAsync()
    {
        WpfClipboard.Clear();
        return Task.CompletedTask;
    }
}

using System.IO;
using System.Windows.Media.Imaging;
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

    public Task<bool> ContainsImageAsync()
    {
        return Task.FromResult(WpfClipboard.ContainsImage());
    }

    public Task<byte[]?> GetImagePngAsync()
    {
        if (!WpfClipboard.ContainsImage())
            return Task.FromResult<byte[]?>(null);

        var image = WpfClipboard.GetImage();
        if (image == null)
            return Task.FromResult<byte[]?>(null);

        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(ms);
        return Task.FromResult<byte[]?>(ms.ToArray());
    }

    public Task SetImagePngAsync(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count > 0)
            WpfClipboard.SetImage(decoder.Frames[0]);
        return Task.CompletedTask;
    }
}

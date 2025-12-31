using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class ScreenService : IScreenService
{
    public ScreenBounds GetPrimaryScreenBounds()
    {
        var screen = GetPrimaryScreen();
        if (screen == null)
            return new ScreenBounds(0, 0, 1920, 1080); // Fallback

        var bounds = screen.Bounds;
        return new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public ScreenBounds GetScreenFromPoint(double x, double y)
    {
        var screens = GetScreens();
        if (screens == null)
            return GetPrimaryScreenBounds();

        var point = new PixelPoint((int)x, (int)y);
        var screen = screens.ScreenFromPoint(point);

        if (screen == null)
            return GetPrimaryScreenBounds();

        var bounds = screen.Bounds;
        return new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public ScreenBounds GetPrimaryWorkingArea()
    {
        var screen = GetPrimaryScreen();
        if (screen == null)
            return new ScreenBounds(0, 0, 1920, 1080);

        var workArea = screen.WorkingArea;
        return new ScreenBounds(workArea.X, workArea.Y, workArea.Width, workArea.Height);
    }

    public IReadOnlyList<ScreenBounds> GetAllScreens()
    {
        var screens = GetScreens();
        if (screens == null)
            return new[] { GetPrimaryScreenBounds() };

        return screens.All
            .Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height))
            .ToList();
    }

    private static Screens? GetScreens()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Screens;
        }
        return null;
    }

    private static Screen? GetPrimaryScreen()
    {
        return GetScreens()?.Primary;
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TerminalHost;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Temporary: Create a simple window to verify build works
            desktop.MainWindow = new Avalonia.Controls.Window
            {
                Title = "TerminalHost - Build Verification",
                Width = 800,
                Height = 600,
                Content = new Avalonia.Controls.TextBlock
                {
                    Text = "Stage 1 Complete: Build system working!",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 24
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.WebView.Desktop;
using TerminalHost.Core.Domain;

namespace TerminalHost;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle Claude Code hook callbacks early (before any Avalonia initialization)
        var parsedArgs = CommandLineArgs.Parse(args);
        if (parsedArgs.IsHookMode)
        {
            HandleHookAndExit(parsedArgs.HookType!);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Register the app-bundled Nerd Fonts as global fallbacks so any Avalonia
            // text widget (e.g. TerminalHost's own statusline, dialogs, panels) can
            // resolve CLI icon glyphs that the macOS / Intel system fonts lack. The
            // terminal control already uses these directly; this makes the rest of
            // the UI consistent.
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("avares://host/Assets/Fonts#Cascadia Code NF") },
                    new FontFallback { FontFamily = new FontFamily("avares://host/Assets/Fonts#Symbols Nerd Font Mono") },
                }
            })
            .LogToTrace();
#if !LINUX
        builder = builder.UseDesktopWebView();
#endif
        return builder;
    }

    /// <summary>
    /// Handle a Claude Code hook: read JSON from stdin, send to the running main instance via named pipe, exit.
    /// </summary>
    private static void HandleHookAndExit(string hookType)
    {
        try
        {
            var stdinJson = ReadStdinWithTimeout(TimeSpan.FromSeconds(2));
            if (string.IsNullOrWhiteSpace(stdinJson))
            {
                Environment.Exit(0);
                return;
            }

            var hookData = HookEventData.Parse(stdinJson);
            if (hookData == null)
            {
                Environment.Exit(1);
                return;
            }

            HookEvent? hookEvent = hookType switch
            {
                "session-start" => HookEvent.CreateSessionStart(hookData),
                "session-stop" => HookEvent.CreateSessionStop(hookData),
                "session-end" => HookEvent.CreateSessionEnd(hookData),
                "tool-start" => HookEvent.CreateToolStart(hookData),
                "tool-end" => HookEvent.CreateToolEnd(hookData),
                "tool-error" => HookEvent.CreateToolError(hookData),
                "subagent-start" => HookEvent.CreateSubagentStart(hookData),
                "subagent-stop" => HookEvent.CreateSubagentStop(hookData),
                "notification" => HookEvent.CreateNotification(hookData),
                _ => null
            };

            if (hookEvent == null)
            {
                Environment.Exit(0);
                return;
            }

            // Send to running main instance via named pipe
            SendHookToMainInstance(hookEvent);
            Environment.Exit(0);
        }
        catch
        {
            Environment.Exit(1);
        }
    }

    private static void SendHookToMainInstance(HookEvent hookEvent)
    {
        var pipeName = Path.Combine(Path.GetTempPath(), "TerminalHost_IPC_Pipe");
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);
            using var writer = new StreamWriter(client);
            writer.Write(JsonSerializer.Serialize(hookEvent));
            writer.Flush();
        }
        catch
        {
            // Main instance not running — silently ignore
        }
    }

    private static string? ReadStdinWithTimeout(TimeSpan timeout)
    {
        if (!Console.IsInputRedirected)
            return null;

        var result = new System.Text.StringBuilder();
        var readTask = Task.Run(() =>
        {
            var buffer = new char[4096];
            int charsRead;
            while ((charsRead = Console.In.Read(buffer, 0, buffer.Length)) > 0)
                result.Append(buffer, 0, charsRead);
        });

        readTask.Wait(timeout);
        return result.Length > 0 ? result.ToString() : null;
    }
}

#if WINDOWS
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

/// <summary>
/// Avalonia <see cref="NativeControlHost"/> that embeds a WPF <see cref="EasyTerminalControl"/>
/// via a child <see cref="HwndSource"/>. This is how the Avalonia host gets a real ConPTY
/// terminal on Windows without forking the rendering code path in MacTerminalControl.
/// </summary>
/// <remarks>
/// The embedding works in three steps:
///   1. Avalonia calls <see cref="CreateNativeControlCore"/> with the parent top-level HWND.
///   2. We create an <see cref="HwndSource"/> as a child window of that HWND (WS_CHILD style).
///      WPF gives HwndSource its own message pump and dispatcher; the dispatcher is the same
///      one Avalonia uses for the UI thread, so cross-framework reentry is straightforward.
///   3. <see cref="HwndSource.RootVisual"/> is set to the WPF terminal control. WPF runs its
///      own layout/render inside the child HWND; Avalonia keeps responsibility for sizing
///      the HWND via the NativeControlHost host machinery.
/// </remarks>
public sealed class WindowsTerminalControl : NativeControlHost, ITerminalControl, IDisposable
{
    private HwndSource? _hwndSource;
    private EasyTerminalControl? _terminal;
    private string? _command;
    private bool _disposed;

    public object NativeControl => this;
    public new bool IsFocused => base.IsFocused || (_terminal?.IsFocused ?? false);
    public bool IsProcessRunning
    {
        get
        {
            var proc = _terminal?.ConPTYTerm?.Process;
            return proc != null && !proc.HasExited;
        }
    }
    // IProcess (the EasyTerminalControl abstraction) exposes Kill/HasExited/WaitForExit but
    // not ExitCode. Return -1 as a sentinel once exited, null while still running.
    public int? ExitCode
    {
        get
        {
            var proc = _terminal?.ConPTYTerm?.Process;
            return proc != null && proc.HasExited ? -1 : (int?)null;
        }
    }

    public new event EventHandler? Loaded;
    public event Action<string>? OutputReceived;
    public event EventHandler<TerminalMouseEventArgs>? MouseClicked;
    public event EventHandler<int>? ProcessExited;
    public event EventHandler? Resized;

    /// <summary>
    /// Configure the terminal's launch command. Must be called before the control is attached
    /// to a top-level (i.e. before <see cref="CreateNativeControlCore"/> fires).
    /// </summary>
    public Task InitializeAsync(string command, string workingDirectory, System.Collections.Generic.IEnumerable<string>? customPaths = null)
    {
        // workingDirectory and customPaths are folded into the command string by the factory
        // via ICommandComposer.WithWorkingDirectory before we ever see them — EasyTerminalControl
        // doesn't have separate parameters for them.
        _command = command;
        return Task.CompletedTask;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var parameters = new HwndSourceParameters("TerminalHost.WindowsTerminalControl")
        {
            ParentWindow = parent.Handle,
            // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS
            WindowStyle = unchecked((int)0x56000000),
            // 32x32 placeholder — the NativeControlHost will resize us once layout settles.
            Width = 32,
            Height = 32,
        };
        _hwndSource = new HwndSource(parameters);

        _terminal = new EasyTerminalControl
        {
            StartupCommandLine = _command ?? "cmd.exe",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            FontFamilyWhenSettingTheme = new System.Windows.Media.FontFamily("Cascadia Code NF, Cascadia Code, Consolas"),
            FontSizeWhenSettingTheme = 12,
            MinHeight = 32,
            MinWidth = 32,
        };

        _terminal.Loaded += OnTerminalLoaded;
        _hwndSource.RootVisual = _terminal;

        return new PlatformHandle(_hwndSource.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        TeardownTerminal();
        _hwndSource?.Dispose();
        _hwndSource = null;
        base.DestroyNativeControlCore(control);
    }

    private void OnTerminalLoaded(object sender, RoutedEventArgs e)
    {
        var terminal = _terminal;
        if (terminal == null) return;

        // Defer until the ConPTY has had a chance to spin up — mirrors what the WPF host's
        // factory does in TerminalHost\Services\TerminalControlFactory.cs.
        terminal.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(100);

            if (terminal.ConPTYTerm != null)
            {
                var proc = terminal.ConPTYTerm.Process;
                if (proc == null || proc.HasExited)
                {
                    try { await terminal.RestartTerm(); }
                    catch { /* swallow — the next interaction will surface the failure */ }
                    await Task.Delay(200);
                }

                // Hook output interception for ITerminalControl.OutputReceived.
                // The library's InterceptDelegate signature is (ref Span<char> str) and is
                // invoked synchronously from the ConPTY read loop; we materialize a string and
                // dispatch the event back to subscribers.
                try
                {
                    terminal.ConPTYTerm.InterceptOutputToUITerminal = (ref Span<char> data) =>
                    {
                        if (data.Length > 0)
                            OutputReceived?.Invoke(new string(data));
                    };
                }
                catch { /* output interception is best-effort */ }

                // Wire process-exit forwarding. IProcess doesn't surface an Exited event, so
                // we block a background thread on WaitForExit() and forward the transition.
                try
                {
                    var exitProc = terminal.ConPTYTerm.Process;
                    if (exitProc != null)
                    {
                        Task.Run(() =>
                        {
                            try { exitProc.WaitForExit(); }
                            catch { /* process disposed before we could wait */ }
                            try { ProcessExited?.Invoke(this, -1); }
                            catch { /* event handlers shouldn't crash the pump thread */ }
                        });
                    }
                }
                catch { /* exit forwarding is best-effort */ }

                ApplyDefaultTheme(terminal);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);

        // Re-raise on the Avalonia dispatcher so ITerminalControl consumers see it on
        // the thread they expect.
        Dispatcher.UIThread.Post(() => Loaded?.Invoke(this, EventArgs.Empty));
    }

    private static void ApplyDefaultTheme(EasyTerminalControl terminal)
    {
        try
        {
            var theme = new TerminalTheme
            {
                DefaultBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x0C, 0x0C, 0x0C)),
                DefaultForeground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                DefaultSelectionBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x26, 0x4F, 0x78)),
                CursorStyle = CursorStyle.BlinkingBar,
                ColorTable =
                [
                    0x0C0C0C, 0xDA3700, 0x0EA113, 0xDD963A,
                    0x1F0FC5, 0x981788, 0x009CC1, 0xCCCCCC,
                    0x767676, 0xFF783B, 0x0CC616, 0xD6D661,
                    0x5648E7, 0x9E00B4, 0xA5F1F9, 0xF2F2F2,
                ],
            };
            terminal.Theme = theme;
        }
        catch { /* theming is best-effort */ }
    }

    public void WriteToTerminal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        WriteToTerminal(text.AsSpan());
    }

    public void WriteToTerminal(ReadOnlySpan<char> text)
    {
        var pty = _terminal?.ConPTYTerm;
        if (pty == null) return;

        try
        {
            pty.WriteToTerm(text);
        }
        catch { /* swallow — the user just typed something and the pty died, nothing to do */ }
    }

    public string GetSelectedText()
    {
        try
        {
            return _terminal?.Terminal?.GetSelectedText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    void ITerminalControl.Focus()
    {
        // Move keyboard focus into the embedded WPF surface. NativeControlHost.Focus()
        // alone parks focus on the Avalonia wrapper — the inner WPF control also needs it
        // for keyboard input to reach the ConPTY.
        base.Focus();
        try { _terminal?.Focus(); }
        catch { /* WPF focus calls can throw mid-teardown */ }
    }

    public async Task RestartAsync()
    {
        var terminal = _terminal;
        if (terminal == null) return;

        await terminal.Dispatcher.InvokeAsync(async () =>
        {
            try { await terminal.RestartTerm(); }
            catch { /* restart failures surface via subsequent IsProcessRunning checks */ }
        });
    }

    private void TeardownTerminal()
    {
        var terminal = _terminal;
        if (terminal != null)
        {
            terminal.Loaded -= OnTerminalLoaded;
            try
            {
                var proc = terminal.ConPTYTerm?.Process;
                if (proc != null && !proc.HasExited)
                    proc.Kill();
            }
            catch { /* kill is best-effort during teardown */ }
        }
        _terminal = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TeardownTerminal();
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
#endif

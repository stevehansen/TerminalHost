// Copyright (c) TerminalHost. All rights reserved.
// COM server entry point for the PowerToys Command Palette extension.

using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace TerminalHost.CmdPal;

/// <summary>
/// Entry point. When launched with -RegisterProcessAsComServer, acts as an
/// out-of-process COM server hosting the TerminalHost extension class.
/// </summary>
public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            var extensionDisposedEvent = new ManualResetEvent(false);

            var server = new ComServer();
            server.RegisterClass<TerminalHostExtension, IExtension>();
            server.Start();

            extensionDisposedEvent.WaitOne();

            server.Stop();
            server.UnsafeDispose();
        }
    }
}

// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Commands;

/// <summary>
/// Opens a folder in TerminalHost using the host.exe CLI.
/// Uses a form (Adaptive Card) to accept the folder path.
/// </summary>
internal sealed partial class OpenProjectCommand : InvokableCommand
{
    public OpenProjectCommand()
    {
        Name = "Open Project";
        Id = "com.terminalhost.cmdpal.openproject";
        Icon = new IconInfo("\uE8DA"); // OpenFolderHorizontal
    }

    public override ICommandResult Invoke()
    {
        // Launch host.exe with current directory — user can also type a path
        // in CmdPal's search. For now, just focus the window which will
        // trigger the folder picker if no tabs are open.
        HostCli.FocusWindow();
        return CommandResult.Dismiss();
    }
}

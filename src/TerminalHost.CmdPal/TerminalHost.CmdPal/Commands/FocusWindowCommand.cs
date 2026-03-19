// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Commands;

/// <summary>
/// Brings the TerminalHost window to the foreground.
/// </summary>
internal sealed partial class FocusWindowCommand : InvokableCommand
{
    public FocusWindowCommand()
    {
        Name = "Focus Window";
        Id = "com.terminalhost.cmdpal.focuswindow";
        Icon = new IconInfo("\uE8A7"); // OpenInNewWindow
    }

    public override ICommandResult Invoke()
    {
        HostCli.FocusWindow();
        return CommandResult.Dismiss();
    }
}

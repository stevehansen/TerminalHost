// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Commands;

/// <summary>
/// Focuses a specific TerminalHost tab by its working directory.
/// Uses host.exe CLI which detects existing tabs and focuses them.
/// </summary>
internal sealed partial class SwitchTabCommand : InvokableCommand
{
    private readonly string _workingDirectory;

    public SwitchTabCommand(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        Name = "Switch Tab";
        Id = $"com.terminalhost.cmdpal.switchtab.{workingDirectory.GetHashCode():X8}";
    }

    public override ICommandResult Invoke()
    {
        HostCli.OpenProject(_workingDirectory);
        return CommandResult.Dismiss();
    }
}

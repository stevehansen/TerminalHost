using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Feature-area contributor for the command palette. Each provider returns
/// the commands its feature owns; <see cref="ICommandPalette"/> stitches them
/// together. Providers may gate on <see cref="ICommandContext.HasService{T}"/>
/// and return zero commands when their backing service is null.
/// </summary>
public interface ICommandProvider
{
    IEnumerable<PaletteCommand> GetCommands(ICommandContext ctx);
}

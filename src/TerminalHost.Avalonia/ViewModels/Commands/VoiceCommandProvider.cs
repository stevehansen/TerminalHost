using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Voice category provider — placeholder for parity with the WPF host.
/// Avalonia does not yet surface voice command palette entries (the underlying
/// <see cref="MainViewModel.ToggleVoiceListening"/> exists but has no palette
/// commands wired up). This provider returns no commands; it exists so the
/// provider list shape matches across hosts and the seam is ready when voice
/// commands ship on macOS.
/// </summary>
internal sealed class VoiceCommandProvider : ICommandProvider
{
    public VoiceCommandProvider(MainViewModel vm)
    {
        _ = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IEnumerable<PaletteCommand> GetCommands(ICommandContext ctx) => [];
}

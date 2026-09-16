using TerminalHost.Core.ViewModels;

namespace TerminalHost;

/// <summary>
/// Command palette popup logic.
/// </summary>
public partial class MainWindow
{
    private void ShowCommandPalette()
    {
        _panelRouter?.Show<CommandPaletteViewModel>();
    }
}

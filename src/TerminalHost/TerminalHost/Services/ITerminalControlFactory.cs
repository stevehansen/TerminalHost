using EasyWindowsTerminalControl;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface ITerminalControlFactory
{
    EasyTerminalControl CreateTerminalControl(TerminalSession session);
}

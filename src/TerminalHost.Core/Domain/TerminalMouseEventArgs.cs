using System;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Mouse event arguments for terminal control.
/// </summary>
public class TerminalMouseEventArgs : EventArgs
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsLeftButton { get; init; }
    public bool IsRightButton { get; init; }
    public bool IsCtrlPressed { get; init; }
    public bool IsShiftPressed { get; init; }
}

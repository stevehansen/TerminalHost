using System.Runtime.InteropServices;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of taskbar progress service using ITaskbarList3 COM interface.
/// Shows colored glows on the taskbar icon to notify users of terminal activity.
/// </summary>
public sealed class TaskbarProgressService : ITaskbarProgressService
{
    private ITaskbarList3? _taskbarList;
    private IntPtr _windowHandle;
    private bool _isEnabled = true;
    private bool _isAudioEnabled = true;
    private TaskbarProgressState _currentState = TaskbarProgressState.NoProgress;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public bool IsAudioEnabled
    {
        get => _isAudioEnabled;
        set => _isAudioEnabled = value;
    }

    /// <summary>
    /// Initializes the service with the main window handle.
    /// Must be called before using the service.
    /// </summary>
    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;

        try
        {
            // Create the ITaskbarList3 COM object (Windows 7+)
            var taskbarType = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"));
            _taskbarList = taskbarType != null ? (ITaskbarList3?)Activator.CreateInstance(taskbarType) : null;
            _taskbarList?.HrInit();
        }
        catch
        {
            // COM creation failed - disable the service
            _isEnabled = false;
            _taskbarList = null;
        }
    }

    public void ShowAmberGlow()
    {
        if (!_isEnabled || _taskbarList == null || _windowHandle == IntPtr.Zero)
            return;

        // Only update if state changed
        if (_currentState == TaskbarProgressState.Paused)
            return;

        try
        {
            // Set to paused state (amber/yellow glow)
            _taskbarList.SetProgressState(_windowHandle, TBPFLAG.TBPF_PAUSED);
            _taskbarList.SetProgressValue(_windowHandle, 50, 100); // 50% for visual effect
            _currentState = TaskbarProgressState.Paused;

            // Play audio notification
            if (_isAudioEnabled)
            {
                PlayWaitingSound();
            }
        }
        catch
        {
            // Ignore COM errors
        }
    }

    public void ShowIndeterminate()
    {
        if (!_isEnabled || _taskbarList == null || _windowHandle == IntPtr.Zero)
            return;

        // Only update if state changed
        if (_currentState == TaskbarProgressState.Indeterminate)
            return;

        try
        {
            // Set to indeterminate state (looping green progress animation)
            _taskbarList.SetProgressState(_windowHandle, TBPFLAG.TBPF_INDETERMINATE);
            _currentState = TaskbarProgressState.Indeterminate;
        }
        catch
        {
            // Ignore COM errors
        }
    }

    public void ShowGreenGlow()
    {
        if (!_isEnabled || _taskbarList == null || _windowHandle == IntPtr.Zero)
            return;

        // Only update if state changed
        if (_currentState == TaskbarProgressState.Normal)
            return;

        try
        {
            // Set to normal state (green glow) at 100%
            _taskbarList.SetProgressState(_windowHandle, TBPFLAG.TBPF_NORMAL);
            _taskbarList.SetProgressValue(_windowHandle, 100, 100);
            _currentState = TaskbarProgressState.Normal;

            // Play audio notification
            if (_isAudioEnabled)
            {
                PlayCompletedSound();
            }
        }
        catch
        {
            // Ignore COM errors
        }
    }

    public void ClearGlow()
    {
        if (!_isEnabled || _taskbarList == null || _windowHandle == IntPtr.Zero)
            return;

        // Only update if we have an active state
        if (_currentState == TaskbarProgressState.NoProgress)
            return;

        try
        {
            _taskbarList.SetProgressState(_windowHandle, TBPFLAG.TBPF_NOPROGRESS);
            _currentState = TaskbarProgressState.NoProgress;
        }
        catch
        {
            // Ignore COM errors
        }
    }

    /// <summary>
    /// Plays a short sound to indicate Claude is waiting for input.
    /// Uses system asterisk sound (subtle notification).
    /// </summary>
    private static void PlayWaitingSound()
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Ignore audio playback errors
        }
    }

    /// <summary>
    /// Plays a short sound to indicate terminal activity completed.
    /// Uses system beep (subtle notification).
    /// </summary>
    private static void PlayCompletedSound()
    {
        try
        {
            System.Media.SystemSounds.Beep.Play();
        }
        catch
        {
            // Ignore audio playback errors
        }
    }

    #region ITaskbarList3 COM Interop

    /// <summary>
    /// Taskbar progress state tracking.
    /// </summary>
    private enum TaskbarProgressState
    {
        NoProgress,
        Indeterminate,
        Normal,
        Paused
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig]
        void HrInit();
        [PreserveSig]
        void AddTab(IntPtr hwnd);
        [PreserveSig]
        void DeleteTab(IntPtr hwnd);
        [PreserveSig]
        void ActivateTab(IntPtr hwnd);
        [PreserveSig]
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig]
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig]
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig]
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
    }

    [Flags]
    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0x0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }

    #endregion
}

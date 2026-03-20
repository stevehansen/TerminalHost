using System;
using System.Runtime.InteropServices;

namespace TerminalHost.Services;

/// <summary>
/// Hooks into macOS's applicationShouldHandleReopen:hasVisibleWindows: via ObjC runtime
/// to detect dock icon clicks and restore minimized windows.
/// </summary>
internal static class MacOsDockHelper
{
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr sel);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr imp);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    // ObjC signature: -(BOOL)applicationShouldHandleReopen:(NSApplication *)app hasVisibleWindows:(BOOL)flag
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ReopenDelegate(IntPtr self, IntPtr sel, IntPtr app, byte hasVisibleWindows);

    // Must be kept alive to prevent GC from collecting the callback
    private static ReopenDelegate? _reopenCallback;
    private static Action? _onDockClicked;

    public static void RegisterDockClickHandler(Action onDockClicked)
    {
        _onDockClicked = onDockClicked;
        _reopenCallback = HandleReopen;
        var funcPtr = Marshal.GetFunctionPointerForDelegate(_reopenCallback);

        // Get [NSApplication sharedApplication]
        var nsAppClass = objc_getClass("NSApplication");
        var sharedAppSel = sel_registerName("sharedApplication");
        var nsApp = objc_msgSend(nsAppClass, sharedAppSel);

        // Get its delegate
        var delegateSel = sel_registerName("delegate");
        var appDelegate = objc_msgSend(nsApp, delegateSel);

        // Get the delegate's class
        var classSel = sel_registerName("class");
        var delegateClass = objc_msgSend(appDelegate, classSel);

        // Register our handler for applicationShouldHandleReopen:hasVisibleWindows:
        var reopenSel = sel_registerName("applicationShouldHandleReopen:hasVisibleWindows:");
        var existingMethod = class_getInstanceMethod(delegateClass, reopenSel);
        if (existingMethod != IntPtr.Zero)
        {
            method_setImplementation(existingMethod, funcPtr);
        }
        else
        {
            // Type encoding: B = BOOL return, @ = id (self), : = SEL (_cmd), @ = NSApplication*, B = BOOL
            class_addMethod(delegateClass, reopenSel, funcPtr, "B@:@B");
        }
    }

    private static byte HandleReopen(IntPtr self, IntPtr sel, IntPtr app, byte hasVisibleWindows)
    {
        _onDockClicked?.Invoke();
        return 1; // YES — let macOS also perform default behavior
    }
}

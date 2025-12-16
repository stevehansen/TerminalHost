using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Shouldly;
using Xunit;

namespace TerminalHost.UITests;

public class ProjectLaunchTests : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;
    private readonly string _tempProjectDir;

    public ProjectLaunchTests()
    {
        // Create a temp directory to use as a project
        _tempProjectDir = Path.Combine(Path.GetTempPath(), "TerminalHostTestProject_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempProjectDir);

        var appPath = FindAppPath();
        
        // Launch with the project directory as argument
        _app = Application.Launch(appPath, _tempProjectDir);
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation);
    }

    private string FindAppPath()
    {
        // Same logic as SmokeTests
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var rootDir = Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
        
        if (rootDir == null) throw new DirectoryNotFoundException("Could not find root directory");

        var appPath = Path.Combine(rootDir, "src", "TerminalHost", "TerminalHost", "bin", "Debug", "net8.0-windows", "win-x64", "host.exe");

        if (!File.Exists(appPath))
        {
            appPath = Path.Combine(rootDir, "src", "TerminalHost", "TerminalHost", "bin", "Debug", "net8.0-windows", "host.exe");
        }

        if (!File.Exists(appPath))
        {
             throw new FileNotFoundException($"Could not find host.exe at {appPath}. Make sure to build the project first.");
        }

        return appPath;
    }

    [Fact]
    public void Launching_With_Directory_Opens_TerminalPair()
    {
        _app.WaitWhileBusy();
        Thread.Sleep(1000); // Give it a moment to load the profile and create tabs

        // Verify window title contains directory name (or "TerminalHost")
        // Title binding might include project name
        // _window.Title.ShouldContain(Path.GetFileName(_tempProjectDir)); 
        // (Title might be complex, let's rely on finding content)

        // Find the specific terminal elements we tagged
        var customBtn = _window.FindFirstDescendant(cf => cf.ByAutomationId("CustomTerminalButton"));
        var shellBtn = _window.FindFirstDescendant(cf => cf.ByAutomationId("ShellTerminalButton"));
        
        // ContentPresenter might be hard to find by ID if it's not a control type UIA exposes easily.
        // Instead, look for the unique text labels inside the terminal headers.
        // var customLabel = _window.FindFirstDescendant(cf => cf.ByName(" Claude Code")); // StringFormat might make name " Claude Code" or icon + " Claude Code"
        
        // Let's retry finding the ContentPresenter but with more patience or specific type.
        // Or better, check for the "Test terminal below" text which should NOT be visible.
        var emptyStateText = _window.FindFirstDescendant(cf => cf.ByName("Test terminal below - "));
        emptyStateText.ShouldBeNull("Empty state should not be visible when project is loaded");

        customBtn.ShouldNotBeNull("Custom terminal button should be visible");
        shellBtn.ShouldNotBeNull("Shell terminal button should be visible");
    }

    public void Dispose()
    {
        _automation.Dispose();
        _app.Close();
        _app.Dispose();

        try 
        {
            if (Directory.Exists(_tempProjectDir))
                Directory.Delete(_tempProjectDir, true);
        }
        catch { /* ignore cleanup errors */ }
    }
}
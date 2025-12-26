using Moq;
using Shouldly;
using TerminalHost.Domain;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Integration;

public class TerminalIntegrationTests
{
    [Fact(Skip = "Requires macOS environment with Avalonia runtime")]
    public async Task TerminalFactory_CanCreateTerminal()
    {
        // This test requires the full Avalonia/Pty.Net stack
        // Run manually on macOS with GUI

        var fileSystem = new FileSystem();
        var dialogService = new Mock<IDialogService>().Object;
        var systemInfo = new SystemInfoService();
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(c => c.Load()).Returns(new AppConfiguration());

        var factory = new TerminalControlFactory(fileSystem, dialogService, systemInfo, configurationService.Object);

        var statisticsService = new Mock<IStatisticsService>().Object;
        var clipboardService = new Mock<IClipboardService>().Object;

        var session = new TerminalSession(
            new Profile { Command = "/bin/zsh", WorkingDir = "~" },
            statisticsService,
            clipboardService,
            "test");

        var control = await factory.CreateTerminalControlAsync(session);

        control.ShouldNotBeNull();
        control.IsProcessRunning.ShouldBeTrue();

        // Cleanup
        control.Kill();
    }

    [Fact]
    public void SystemInfoService_IntegrationTest()
    {
        // Verify all system info methods work together (except font methods which require Avalonia runtime)
        var service = new SystemInfoService();

        var appDataPath = service.GetApplicationDataPath();
        var homePath = service.GetUserHomePath();
        var tempPath = service.GetTempPath();
        var shell = service.GetDefaultShell();

        // All paths should be valid
        appDataPath.ShouldStartWith(homePath);
        tempPath.ShouldNotBeNullOrEmpty();
        shell.ShouldNotBeNullOrEmpty();

        // App data path should contain expected structure
        appDataPath.ShouldContain("Library");
        appDataPath.ShouldContain("Application Support");
        appDataPath.ShouldContain("TerminalHost");
    }

    [Fact]
    public void ProcessService_CanExecuteBasicCommands()
    {
        var service = new ProcessService();

        // Test that we can start a simple command without throwing
        Should.NotThrow(() =>
        {
            service.Start("echo", "test");
        });
    }
}

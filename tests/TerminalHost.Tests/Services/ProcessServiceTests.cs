using Shouldly;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class ProcessServiceTests
{
    [Fact]
    public void OpenFolder_DoesNotThrow()
    {
        var service = new ProcessService();
        var tempDir = Path.GetTempPath();

        // Should not throw
        Should.NotThrow(() => service.OpenFolder(tempDir));
    }

    [Fact]
    public void RevealInFinder_DoesNotThrow()
    {
        var service = new ProcessService();
        var tempFile = Path.GetTempFileName();

        try
        {
            Should.NotThrow(() => service.RevealInFinder(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void OpenUrl_DoesNotThrow()
    {
        var service = new ProcessService();

        // Use a local URL to avoid opening browser
        Should.NotThrow(() => service.OpenUrl("about:blank"));
    }

    [Fact]
    public void OpenWithDefaultApp_DoesNotThrow()
    {
        var service = new ProcessService();
        var tempFile = Path.GetTempFileName();

        try
        {
            Should.NotThrow(() => service.OpenWithDefaultApp(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

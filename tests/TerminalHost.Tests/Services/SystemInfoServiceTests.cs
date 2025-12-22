using Shouldly;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class SystemInfoServiceTests
{
    private readonly SystemInfoService _service = new();

    [Fact]
    public void GetApplicationDataPath_ReturnsValidPath()
    {
        var path = _service.GetApplicationDataPath();

        path.ShouldNotBeNullOrEmpty();
        path.ShouldContain("TerminalHost");
        path.ShouldContain("Library/Application Support");
    }

    [Fact]
    public void GetUserHomePath_ReturnsValidPath()
    {
        var path = _service.GetUserHomePath();

        path.ShouldNotBeNullOrEmpty();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void GetTempPath_ReturnsValidPath()
    {
        var path = _service.GetTempPath();

        path.ShouldNotBeNullOrEmpty();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void GetDefaultShell_ReturnsExistingShell()
    {
        var shell = _service.GetDefaultShell();

        shell.ShouldNotBeNullOrEmpty();
        File.Exists(shell).ShouldBeTrue();
    }

    [Fact(Skip = "Requires Avalonia runtime for FontManager")]
    public void GetInstalledFontFamilies_ReturnsNonEmpty()
    {
        var fonts = _service.GetInstalledFontFamilies().ToList();

        fonts.ShouldNotBeEmpty();
    }

    [Fact(Skip = "Requires Avalonia runtime for FontManager")]
    public void IsFontInstalled_ReturnsTrueForSystemFont()
    {
        // SF Mono or Menlo should be available on macOS
        var hasSfMono = _service.IsFontInstalled("SF Mono");
        var hasMenlo = _service.IsFontInstalled("Menlo");

        // At least one of these fonts should be installed
        (hasSfMono || hasMenlo).ShouldBeTrue();
    }

    [Fact(Skip = "Requires Avalonia runtime for FontManager")]
    public void IsFontInstalled_ReturnsFalseForNonExistentFont()
    {
        var result = _service.IsFontInstalled("NonExistentFontName12345");

        result.ShouldBeFalse();
    }
}

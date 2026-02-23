using Moq;
using Shouldly;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class InvisibleChangeDetectionTests
{
    private readonly InvisibleChangeService _service;

    public InvisibleChangeDetectionTests()
    {
        var fileSystemMock = new Mock<IFileSystem>();
        var gitRunnerMock = new Mock<IGitProcessRunner>();
        _service = new InvisibleChangeService(fileSystemMock.Object, gitRunnerMock.Object);
    }

    [Fact]
    public void Detect_NullDiff_ReturnsNull()
    {
        _service.Detect(null).ShouldBeNull();
    }

    [Fact]
    public void Detect_EmptyDiff_ReturnsNull()
    {
        _service.Detect("").ShouldBeNull();
    }

    [Fact]
    public void Detect_NoDiffContent_ReturnsNull()
    {
        var diff = "diff --git a/file.txt b/file.txt\nindex 123..456 789\n--- a/file.txt\n+++ b/file.txt\n";
        _service.Detect(diff).ShouldBeNull();
    }

    [Fact]
    public void Detect_EolChange_CrlfToLf()
    {
        // Deletions end with \r (CRLF lines split on \n leave trailing \r)
        // Additions do not (LF lines)
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1,2 +1,2 @@\n" +
                   "-hello\r\n" +
                   "+hello\n" +
                   "-world\r\n" +
                   "+world\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasEolChange.ShouldBeTrue();
        result.OldEol.ShouldBe("CRLF");
        result.NewEol.ShouldBe("LF");
        result.IsEntirelyInvisible.ShouldBeTrue();
        result.Summary.ShouldContain("CRLF");
        result.Summary.ShouldContain("LF");
    }

    [Fact]
    public void Detect_EolChange_LfToCrlf()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1,2 +1,2 @@\n" +
                   "-hello\n" +
                   "+hello\r\n" +
                   "-world\n" +
                   "+world\r\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasEolChange.ShouldBeTrue();
        result.OldEol.ShouldBe("LF");
        result.NewEol.ShouldBe("CRLF");
        result.IsEntirelyInvisible.ShouldBeTrue();
    }

    [Fact]
    public void Detect_BomAdded()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-hello\n" +
                   "+\uFEFFhello\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasBomChange.ShouldBeTrue();
        result.OldHasBom.ShouldBeFalse();
        result.NewHasBom.ShouldBeTrue();
        result.IsEntirelyInvisible.ShouldBeTrue();
        result.Summary.ShouldContain("BOM added");
    }

    [Fact]
    public void Detect_BomRemoved()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-\uFEFFhello\n" +
                   "+hello\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasBomChange.ShouldBeTrue();
        result.OldHasBom.ShouldBeTrue();
        result.NewHasBom.ShouldBeFalse();
        result.IsEntirelyInvisible.ShouldBeTrue();
        result.Summary.ShouldContain("BOM removed");
    }

    [Fact]
    public void Detect_TrailingNewlineRemoved()
    {
        // Old version has trailing newline, new version does not
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-hello\n" +
                   "+hello\n" +
                   "\\ No newline at end of file\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasTrailingNewlineChange.ShouldBeTrue();
        result.OldHasTrailingNewline.ShouldBeTrue();
        result.NewHasTrailingNewline.ShouldBeFalse();
        result.Summary.ShouldContain("Trailing newline removed");
    }

    [Fact]
    public void Detect_TrailingNewlineAdded()
    {
        // Old version has no trailing newline, new version does
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-hello\n" +
                   "\\ No newline at end of file\n" +
                   "+hello\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasTrailingNewlineChange.ShouldBeTrue();
        result.OldHasTrailingNewline.ShouldBeFalse();
        result.NewHasTrailingNewline.ShouldBeTrue();
        result.Summary.ShouldContain("Trailing newline added");
    }

    [Fact]
    public void Detect_RealContentChanges_NoInvisible()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-hello\n" +
                   "+world\n";

        var result = _service.Detect(diff);

        result.ShouldBeNull();
    }

    [Fact]
    public void Detect_MixedRealAndInvisible_NotEntirelyInvisible()
    {
        // EOL change but also real content change
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1,2 +1,2 @@\n" +
                   "-hello\r\n" +
                   "+goodbye\n" +
                   "-world\r\n" +
                   "+world\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasEolChange.ShouldBeTrue();
        result.IsEntirelyInvisible.ShouldBeFalse();
    }

    [Fact]
    public void Detect_CombinedEolAndBom()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-\uFEFFhello\r\n" +
                   "+hello\n";

        var result = _service.Detect(diff);

        result.ShouldNotBeNull();
        result.HasEolChange.ShouldBeTrue();
        result.HasBomChange.ShouldBeTrue();
        result.OldHasBom.ShouldBeTrue();
        result.NewHasBom.ShouldBeFalse();
        result.OldEol.ShouldBe("CRLF");
        result.NewEol.ShouldBe("LF");
        result.IsEntirelyInvisible.ShouldBeTrue();
        result.Summary.ShouldContain("CRLF");
        result.Summary.ShouldContain("BOM removed");
    }

    [Fact]
    public void Detect_HasAnyChange_FalseWhenNoInvisible()
    {
        var diff = "diff --git a/file.txt b/file.txt\n" +
                   "--- a/file.txt\n" +
                   "+++ b/file.txt\n" +
                   "@@ -1 +1 @@\n" +
                   "-foo\n" +
                   "+bar\n";

        var result = _service.Detect(diff);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task DiagnoseEolIssue_AutoCrlfTrue_ReturnsDiagnosis()
    {
        var gitRunnerMock = new Mock<IGitProcessRunner>();
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.autocrlf"))
            .ReturnsAsync("true\n");
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.eol"))
            .ReturnsAsync((string?)null);
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("check-attr"))))
            .ReturnsAsync("file.txt\0text\0unspecified\0file.txt\0eol\0unspecified\0");

        var fs = new Mock<IFileSystem>();
        var svc = new InvisibleChangeService(fs.Object, gitRunnerMock.Object);

        var result = await svc.DiagnoseEolIssueAsync(@"C:\project", "file.txt");

        result.ShouldNotBeNull();
        result.Explanation.ShouldContain("core.autocrlf = true");
        result.FixCommand.ShouldContain("--renormalize");
        result.FixLabel.ShouldBe("Renormalize");
    }

    [Fact]
    public async Task DiagnoseEolIssue_GitattributesText_ReturnsDiagnosis()
    {
        var gitRunnerMock = new Mock<IGitProcessRunner>();
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.autocrlf"))
            .ReturnsAsync("false\n");
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.eol"))
            .ReturnsAsync((string?)null);
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("check-attr"))))
            .ReturnsAsync("file.txt\0text\0auto\0file.txt\0eol\0unspecified\0");

        var fs = new Mock<IFileSystem>();
        var svc = new InvisibleChangeService(fs.Object, gitRunnerMock.Object);

        var result = await svc.DiagnoseEolIssueAsync(@"C:\project", "file.txt");

        result.ShouldNotBeNull();
        result.Explanation.ShouldContain(".gitattributes text=auto");
        result.FixCommand.ShouldContain("--renormalize");
    }

    [Fact]
    public async Task DiagnoseEolIssue_NoConfig_ReturnsNull()
    {
        var gitRunnerMock = new Mock<IGitProcessRunner>();
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.autocrlf"))
            .ReturnsAsync("false\n");
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), "config core.eol"))
            .ReturnsAsync((string?)null);
        gitRunnerMock.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("check-attr"))))
            .ReturnsAsync("file.txt\0text\0unspecified\0file.txt\0eol\0unspecified\0");

        var fs = new Mock<IFileSystem>();
        var svc = new InvisibleChangeService(fs.Object, gitRunnerMock.Object);

        var result = await svc.DiagnoseEolIssueAsync(@"C:\project", "file.txt");

        result.ShouldBeNull();
    }
}

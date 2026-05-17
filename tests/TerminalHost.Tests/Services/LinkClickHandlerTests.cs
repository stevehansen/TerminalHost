using System.Collections.Generic;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class LinkClickHandlerTests
{
    private static (LinkClickHandler handler, Mock<ILinkDetectionService> mock, List<FilePreviewRequestedEventArgs> received) CreateSut()
    {
        var mock = new Mock<ILinkDetectionService>(MockBehavior.Strict);
        var handler = new LinkClickHandler(mock.Object);
        var received = new List<FilePreviewRequestedEventArgs>();
        handler.FilePreviewRequested += (_, args) => received.Add(args);
        return (handler, mock, received);
    }

    [Fact]
    public void Handle_EmptyString_DoesNotFireEventOrCallService()
    {
        var (handler, mock, received) = CreateSut();

        handler.Handle("", "wd");

        received.ShouldBeEmpty();
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Handle_NullString_DoesNotFireEventOrCallService()
    {
        var (handler, mock, received) = CreateSut();

        handler.Handle(null!, "wd");

        received.ShouldBeEmpty();
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Handle_UrlDetected_CallsOpenLinkAndDoesNotFirePreview()
    {
        var (handler, mock, received) = CreateSut();
        const string url = "https://example.com";
        mock.Setup(s => s.DetectLink(url, It.IsAny<string?>())).Returns(url);
        mock.Setup(s => s.IsFilePath(url)).Returns(false);
        mock.Setup(s => s.OpenLink(url));

        handler.Handle(url, "wd");

        mock.Verify(s => s.OpenLink(url), Times.Once);
        received.ShouldBeEmpty();
    }

    [Fact]
    public void Handle_FilePathWithLineAndColumn_FiresPreviewWithParsedPosition()
    {
        var (handler, mock, received) = CreateSut();
        const string link = "src/foo.cs:42:7";
        mock.Setup(s => s.DetectLink(link, It.IsAny<string?>())).Returns(link);
        mock.Setup(s => s.IsFilePath(link)).Returns(true);

        handler.Handle(link, "wd");

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("src/foo.cs");
        received[0].Line.ShouldBe(42);
        received[0].Column.ShouldBe(7);
        mock.Verify(s => s.OpenLink(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Handle_FilePathWithoutPosition_FiresPreviewWithNullLineAndColumn()
    {
        var (handler, mock, received) = CreateSut();
        const string link = "README.md";
        mock.Setup(s => s.DetectLink(link, It.IsAny<string?>())).Returns(link);
        mock.Setup(s => s.IsFilePath(link)).Returns(true);

        handler.Handle(link, "wd");

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("README.md");
        received[0].Line.ShouldBeNull();
        received[0].Column.ShouldBeNull();
        mock.Verify(s => s.OpenLink(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Handle_MultipleLines_ScansBackwardsLastLineWins()
    {
        var (handler, mock, received) = CreateSut();
        mock.Setup(s => s.DetectLink("first.cs", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink("second.cs", It.IsAny<string?>())).Returns("second.cs");
        mock.Setup(s => s.IsFilePath("second.cs")).Returns(true);

        handler.Handle("first.cs\nsecond.cs", "wd");

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("second.cs");
        mock.Verify(s => s.DetectLink("second.cs", It.IsAny<string?>()), Times.Once);
        // First.cs must NOT have been called before second.cs short-circuited.
        mock.Verify(s => s.DetectLink("first.cs", It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Handle_WithinLine_FirstWordToMatchWins()
    {
        // Within a single line, the scan is left-to-right word-by-word.
        // Given "a b c" where both "b" and "c" would match, "b" wins.
        var (handler, mock, received) = CreateSut();
        mock.Setup(s => s.DetectLink("a", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink("b", It.IsAny<string?>())).Returns("b");
        mock.Setup(s => s.DetectLink("c", It.IsAny<string?>())).Returns("c");
        mock.Setup(s => s.IsFilePath("b")).Returns(true);

        handler.Handle("a b c", "wd");

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("b");
        mock.Verify(s => s.DetectLink("c", It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Handle_NoWordMatches_FallsBackToWholeLine()
    {
        var (handler, mock, received) = CreateSut();
        const string line = "a path with spaces.cs";
        mock.Setup(s => s.DetectLink("a", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink("path", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink("with", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink("spaces.cs", It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(s => s.DetectLink(line, It.IsAny<string?>())).Returns(line);
        mock.Setup(s => s.IsFilePath(line)).Returns(true);

        handler.Handle(line, "wd");

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe(line);
    }

    [Fact]
    public void Handle_PassesWorkingDirectoryToDetectLink()
    {
        var (handler, mock, received) = CreateSut();
        const string wd = @"P:\proj";
        mock.Setup(s => s.DetectLink("foo.cs", wd)).Returns("foo.cs");
        mock.Setup(s => s.IsFilePath("foo.cs")).Returns(true);

        handler.Handle("foo.cs", wd);

        received.Count.ShouldBe(1);
        mock.Verify(s => s.DetectLink("foo.cs", It.Is<string?>(v => v == wd)), Times.Once);
    }

    [Fact]
    public void Handle_FirstMatchShortCircuits_NoFurtherDetectLinkCalls()
    {
        var (handler, mock, received) = CreateSut();
        // "ignored" appears on the first line (scanned last since we go backwards),
        // "hit" is on the last line (scanned first). The match on "hit" should
        // short-circuit before "ignored" is ever inspected.
        mock.Setup(s => s.DetectLink("hit", It.IsAny<string?>())).Returns("hit");
        mock.Setup(s => s.IsFilePath("hit")).Returns(true);

        handler.Handle("ignored\nhit", "wd");

        received.Count.ShouldBe(1);
        mock.Verify(s => s.DetectLink("ignored", It.IsAny<string?>()), Times.Never);
    }
}

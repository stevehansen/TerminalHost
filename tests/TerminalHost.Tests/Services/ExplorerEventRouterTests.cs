using System.Collections.Generic;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class ExplorerEventRouterTests
{
    [Fact]
    public void HandleFileViewerRequested_PreviewMode_FiresPreviewWithOpenInEditModeFalse()
    {
        var router = new ExplorerEventRouter();
        var received = new List<FilePreviewRequestedEventArgs>();
        router.FilePreviewRequested += (_, args) => received.Add(args);

        router.HandleFileViewerRequested(new FileViewerRequestedEventArgs
        {
            FilePath = "X",
            Mode = FileViewerMode.Preview
        });

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("X");
        received[0].OpenInEditMode.ShouldBeFalse();
        received[0].Line.ShouldBe(0);
        received[0].Column.ShouldBe(0);
    }

    [Fact]
    public void HandleFileViewerRequested_EditMode_FiresPreviewWithOpenInEditModeTrue()
    {
        var router = new ExplorerEventRouter();
        var received = new List<FilePreviewRequestedEventArgs>();
        router.FilePreviewRequested += (_, args) => received.Add(args);

        router.HandleFileViewerRequested(new FileViewerRequestedEventArgs
        {
            FilePath = "X",
            Mode = FileViewerMode.Edit
        });

        received.Count.ShouldBe(1);
        received[0].FilePath.ShouldBe("X");
        received[0].OpenInEditMode.ShouldBeTrue();
        received[0].Line.ShouldBe(0);
        received[0].Column.ShouldBe(0);
    }

    [Fact]
    public void HandleFileViewerRequested_PreservesFilePath()
    {
        var router = new ExplorerEventRouter();
        FilePreviewRequestedEventArgs? captured = null;
        router.FilePreviewRequested += (_, args) => captured = args;

        const string path = @"C:\foo\bar.cs";
        router.HandleFileViewerRequested(new FileViewerRequestedEventArgs
        {
            FilePath = path,
            Mode = FileViewerMode.Preview
        });

        captured.ShouldNotBeNull();
        captured!.FilePath.ShouldBe(path);
    }

    [Fact]
    public void HandleFileHistoryRequested_ReRaisesSameArgsReference()
    {
        var router = new ExplorerEventRouter();
        var received = new List<FileHistoryRequestedEventArgs>();
        router.FileHistoryRequested += (_, args) => received.Add(args);

        var args = new FileHistoryRequestedEventArgs
        {
            WorkingDirectory = "wd",
            FilePath = "fp"
        };

        router.HandleFileHistoryRequested(args);

        received.Count.ShouldBe(1);
        received[0].ShouldBeSameAs(args);
    }

    [Fact]
    public void HandleFileBlameRequested_ReRaisesSameArgsReference()
    {
        var router = new ExplorerEventRouter();
        var received = new List<FileBlameRequestedEventArgs>();
        router.FileBlameRequested += (_, args) => received.Add(args);

        var args = new FileBlameRequestedEventArgs
        {
            WorkingDirectory = "wd",
            FilePath = "fp"
        };

        router.HandleFileBlameRequested(args);

        received.Count.ShouldBe(1);
        received[0].ShouldBeSameAs(args);
    }

    [Fact]
    public void FilePreviewRequested_MultipleSubscribers_AllReceiveEvent()
    {
        var router = new ExplorerEventRouter();
        var firstCount = 0;
        var secondCount = 0;
        router.FilePreviewRequested += (_, _) => firstCount++;
        router.FilePreviewRequested += (_, _) => secondCount++;

        router.HandleFileViewerRequested(new FileViewerRequestedEventArgs
        {
            FilePath = "X",
            Mode = FileViewerMode.Preview
        });

        firstCount.ShouldBe(1);
        secondCount.ShouldBe(1);
    }

    [Fact]
    public void HandleMethods_WithNoSubscribers_DoNotThrow()
    {
        var router = new ExplorerEventRouter();

        Should.NotThrow(() => router.HandleFileViewerRequested(new FileViewerRequestedEventArgs
        {
            FilePath = "X",
            Mode = FileViewerMode.Preview
        }));

        Should.NotThrow(() => router.HandleFileHistoryRequested(new FileHistoryRequestedEventArgs
        {
            WorkingDirectory = "wd",
            FilePath = "fp"
        }));

        Should.NotThrow(() => router.HandleFileBlameRequested(new FileBlameRequestedEventArgs
        {
            WorkingDirectory = "wd",
            FilePath = "fp"
        }));
    }
}

using System.IO;
using System.Text;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class InvisibleChangeFixTests
{
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly InvisibleChangeService _service;
    private const string WorkDir = @"C:\project";
    private const string FilePath = "file.txt";
    private static readonly string FullPath = Path.Combine(WorkDir, FilePath);

    public InvisibleChangeFixTests()
    {
        _fileSystemMock = new Mock<IFileSystem>();
        _fileSystemMock.Setup(x => x.FileExists(FullPath)).Returns(true);
        var gitRunnerMock = new Mock<IGitProcessRunner>();
        _service = new InvisibleChangeService(_fileSystemMock.Object, gitRunnerMock.Object);
    }

    [Fact]
    public async Task Fix_EolCrlfToLf_RevertsToOriginalCrlf()
    {
        // Current file has LF, old was CRLF → revert to CRLF
        var currentBytes = Encoding.UTF8.GetBytes("hello\nworld\n");
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasEolChange = true,
            OldEol = "CRLF",
            NewEol = "LF"
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        var result = Encoding.UTF8.GetString(writtenBytes);
        result.ShouldBe("hello\r\nworld\r\n");
    }

    [Fact]
    public async Task Fix_EolLfToCrlf_RevertsToOriginalLf()
    {
        // Current file has CRLF, old was LF → revert to LF
        var currentBytes = Encoding.UTF8.GetBytes("hello\r\nworld\r\n");
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasEolChange = true,
            OldEol = "LF",
            NewEol = "CRLF"
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        var result = Encoding.UTF8.GetString(writtenBytes);
        result.ShouldBe("hello\nworld\n");
    }

    [Fact]
    public async Task Fix_BomAdded_RemovesBom()
    {
        // Current file has BOM (was added), old had no BOM → remove BOM
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes("hello");
        var currentBytes = bom.Concat(content).ToArray();
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasBomChange = true,
            OldHasBom = false,
            NewHasBom = true
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        // Should NOT start with BOM
        (writtenBytes.Length >= 3 && writtenBytes[0] == 0xEF && writtenBytes[1] == 0xBB && writtenBytes[2] == 0xBF)
            .ShouldBeFalse();
        Encoding.UTF8.GetString(writtenBytes).ShouldBe("hello");
    }

    [Fact]
    public async Task Fix_BomRemoved_AddsBomBack()
    {
        // Current file has no BOM (was removed), old had BOM → add BOM back
        var currentBytes = Encoding.UTF8.GetBytes("hello");
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasBomChange = true,
            OldHasBom = true,
            NewHasBom = false
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        // Should start with BOM
        writtenBytes.Length.ShouldBeGreaterThan(3);
        writtenBytes[0].ShouldBe((byte)0xEF);
        writtenBytes[1].ShouldBe((byte)0xBB);
        writtenBytes[2].ShouldBe((byte)0xBF);
        Encoding.UTF8.GetString(writtenBytes, 3, writtenBytes.Length - 3).ShouldBe("hello");
    }

    [Fact]
    public async Task Fix_TrailingNewlineRemoved_AddsItBack()
    {
        // Current file has no trailing newline (was removed), old had one → add it back
        var currentBytes = Encoding.UTF8.GetBytes("hello");
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasTrailingNewlineChange = true,
            OldHasTrailingNewline = true,
            NewHasTrailingNewline = false
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        var result = Encoding.UTF8.GetString(writtenBytes);
        result.ShouldEndWith("\n");
    }

    [Fact]
    public async Task Fix_TrailingNewlineAdded_RemovesIt()
    {
        // Current file has trailing newline (was added), old had none → remove it
        var currentBytes = Encoding.UTF8.GetBytes("hello\n");
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasTrailingNewlineChange = true,
            OldHasTrailingNewline = false,
            NewHasTrailingNewline = true
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        var result = Encoding.UTF8.GetString(writtenBytes);
        result.ShouldBe("hello");
    }

    [Fact]
    public async Task Fix_FileDoesNotExist_DoesNothing()
    {
        _fileSystemMock.Setup(x => x.FileExists(FullPath)).Returns(false);

        var info = new InvisibleChangeInfo
        {
            HasEolChange = true,
            OldEol = "CRLF",
            NewEol = "LF"
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        _fileSystemMock.Verify(x => x.ReadAllBytes(It.IsAny<string>()), Times.Never);
        _fileSystemMock.Verify(x => x.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task Fix_CombinedBomAndEol()
    {
        // Current file has BOM + LF, old had no BOM + CRLF → remove BOM, convert to CRLF
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes("hello\nworld\n");
        var currentBytes = bom.Concat(content).ToArray();
        _fileSystemMock.Setup(x => x.ReadAllBytes(FullPath)).Returns(currentBytes);

        byte[]? writtenBytes = null;
        _fileSystemMock.Setup(x => x.WriteAllBytes(FullPath, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, b) => writtenBytes = b);

        var info = new InvisibleChangeInfo
        {
            HasBomChange = true,
            OldHasBom = false,
            NewHasBom = true,
            HasEolChange = true,
            OldEol = "CRLF",
            NewEol = "LF"
        };

        await _service.FixAsync(WorkDir, FilePath, info);

        writtenBytes.ShouldNotBeNull();
        // Should NOT start with BOM
        (writtenBytes[0] == 0xEF && writtenBytes[1] == 0xBB && writtenBytes[2] == 0xBF)
            .ShouldBeFalse();
        var result = Encoding.UTF8.GetString(writtenBytes);
        result.ShouldBe("hello\r\nworld\r\n");
    }
}

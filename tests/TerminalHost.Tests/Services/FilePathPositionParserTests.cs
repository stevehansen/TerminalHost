using Shouldly;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class FilePathPositionParserTests
{
    [Fact]
    public void Parse_PlainPath_ReturnsPathWithNullLineAndColumn()
    {
        var (path, line, column) = FilePathPositionParser.Parse("README.md");

        path.ShouldBe("README.md");
        line.ShouldBeNull();
        column.ShouldBeNull();
    }

    [Fact]
    public void Parse_PathWithLine_ReturnsPathAndLine()
    {
        var (path, line, column) = FilePathPositionParser.Parse("foo.cs:42");

        path.ShouldBe("foo.cs");
        line.ShouldBe(42);
        column.ShouldBeNull();
    }

    [Fact]
    public void Parse_PathWithLineAndColumn_ReturnsAll()
    {
        var (path, line, column) = FilePathPositionParser.Parse("foo.cs:42:7");

        path.ShouldBe("foo.cs");
        line.ShouldBe(42);
        column.ShouldBe(7);
    }

    [Fact]
    public void Parse_WindowsDriveLetter_NoPosition_ReturnsFullPath()
    {
        var (path, line, column) = FilePathPositionParser.Parse(@"C:\path\file.cs");

        path.ShouldBe(@"C:\path\file.cs");
        line.ShouldBeNull();
        column.ShouldBeNull();
    }

    [Fact]
    public void Parse_WindowsDriveLetter_WithLine_ReturnsPathAndLine()
    {
        var (path, line, column) = FilePathPositionParser.Parse(@"C:\path\file.cs:99");

        path.ShouldBe(@"C:\path\file.cs");
        line.ShouldBe(99);
        column.ShouldBeNull();
    }

    [Fact]
    public void Parse_WindowsDriveLetter_WithLineAndColumn_ReturnsAll()
    {
        var (path, line, column) = FilePathPositionParser.Parse(@"C:\path\file.cs:12:34");

        path.ShouldBe(@"C:\path\file.cs");
        line.ShouldBe(12);
        column.ShouldBe(34);
    }

    [Fact]
    public void Parse_NonNumericLine_ReturnsNullLine()
    {
        var (path, line, column) = FilePathPositionParser.Parse("foo.cs:abc");

        path.ShouldBe("foo.cs");
        line.ShouldBeNull();
        column.ShouldBeNull();
    }
}

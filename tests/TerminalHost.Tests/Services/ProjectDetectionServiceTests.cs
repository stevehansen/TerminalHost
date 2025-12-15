using Moq;
using Shouldly;
using System.IO;
using TerminalHost.Domain;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class ProjectDetectionServiceTests
{
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly ProfileRegistry _profileRegistry;
    private readonly ProjectDetectionService _service;
    private const string TestPath = "P:\\TestProject";

    public ProjectDetectionServiceTests()
    {
        _fileSystemMock = new Mock<IFileSystem>();
        _configServiceMock = new Mock<IConfigurationService>();

        // Setup mock config with default project types
        var config = new AppConfiguration(); // Default includes ProjectType.GetDefaults()
        _configServiceMock.Setup(x => x.Load()).Returns(config);

        _profileRegistry = new ProfileRegistry(_configServiceMock.Object);
        _service = new ProjectDetectionService(_profileRegistry, _fileSystemMock.Object);
    }

    [Fact]
    public void DetectProjectType_ShouldReturnDotNet_WhenCsprojExists()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(true);
        // .NET pattern: *.csproj
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new[] { Path.Combine(TestPath, "App.csproj") });
        
        // Ensure other checks return empty
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "package.json", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        // And so on for others... but Moq returns empty array/null by default? No, usually null for reference types if not strict. 
        // Array return type needs explicit setup or it returns empty array/null.
        // I'll be safe and rely on the fact that the first match returns. 
        // .NET has priority 10. Node is 5.
        // So finding csproj should return immediately.

        // Act
        var result = _service.DetectProjectType(TestPath);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("dotnet");
    }

    [Fact]
    public void DetectProjectType_ShouldReturnNode_WhenPackageJsonExists()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(true);
        // Mock no .net files
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
            
        // Mock package.json
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, "package.json", SearchOption.TopDirectoryOnly))
            .Returns(new[] { Path.Combine(TestPath, "package.json") });

        // Act
        var result = _service.DetectProjectType(TestPath);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("nodejs-npm");
    }

    [Fact]
    public void DetectProjectType_ShouldReturnNull_WhenNoMarkerFilesExist()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(true);
        // Mock empty for everything (using wildcard matcher for simplicity in test logic, but specific in code)
        // Since I can't easily wildcard the pattern in Moq effectively without It.IsAny, 
        // I'll just use It.IsAny<string> for pattern
        _fileSystemMock.Setup(x => x.GetFiles(TestPath, It.IsAny<string>(), SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());

        // Act
        var result = _service.DetectProjectType(TestPath);

        // Assert
        result.ShouldBeNull();
    }
}

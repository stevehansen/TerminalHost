using Xunit;
using Moq;
using Shouldly;
using TerminalHost.Services;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace TerminalHost.Tests.Services;

public class ProjectDetectionServiceTests
{
    private readonly Mock<IProfileRegistry> _mockProfileRegistry;
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly ProjectDetectionService _projectDetectionService;

    public ProjectDetectionServiceTests()
    {
        _mockProfileRegistry = new Mock<IProfileRegistry>();
        _mockFileSystem = new Mock<IFileSystem>();

        // Setup default project types
        _mockProfileRegistry.Setup(pr => pr.GetProjectTypes())
            .Returns(ProjectType.GetDefaults());

        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);

        _projectDetectionService = new ProjectDetectionService(_mockProfileRegistry.Object, _mockFileSystem.Object);
    }

    [Fact]
    public void DetectProjectType_ShouldReturnDotNet_WhenCsprojExists()
    {
        // Arrange
        var workingDirectory = "P:\\MyDotNetApp";
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "P:\\MyDotNetApp\\App.csproj" });
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);

        // Act
        var result = _projectDetectionService.DetectProjectType(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("dotnet");
    }

    [Fact]
    public void DetectProjectType_ShouldReturnNodeJs_WhenPackageJsonExists()
    {
        // Arrange
        var workingDirectory = "P:\\MyNodeApp";
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "package.json", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "P:\\MyNodeApp\\package.json" });
        // Ensure dotnet check fails
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);

        // Act
        var result = _projectDetectionService.DetectProjectType(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("nodejs-npm");
    }

    [Fact]
    public void DetectProjectType_ShouldReturnNull_WhenNoMarkerFilesExist()
    {
        // Arrange
        var workingDirectory = "P:\\EmptyFolder";
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, It.IsAny<string>(), SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);

        // Act
        var result = _projectDetectionService.DetectProjectType(workingDirectory);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void DetectProjectType_ShouldPrioritizeDotNetOverNode_WhenBothExist()
    {
        // Arrange
        var workingDirectory = "P:\\MixedApp";
        // Simulate both existing
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "P:\\MixedApp\\App.csproj" });
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "package.json", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "P:\\MixedApp\\package.json" });

        // Act
        var result = _projectDetectionService.DetectProjectType(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("dotnet"); // Priority 10 vs 5
    }

    [Fact]
    public void FindDotNetProject_ShouldReturnNull_WhenSingleRunnableProjectInRoot()
    {
        // Arrange
        var workingDirectory = "P:\\SimpleApp";
        var csprojPath = "P:\\SimpleApp\\SimpleApp.csproj";
        
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new[] { csprojPath });
        
        // Mock runnable project content (Exe)
        var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
        _mockFileSystem.Setup(fs => fs.ReadAllText(csprojPath)).Returns(csprojContent);

        // Act
        var result = _projectDetectionService.FindDotNetProject(workingDirectory);

        // Assert
        result.ShouldBeNull(); // Should be null because dotnet run works automatically
    }

    [Fact]
    public void SuggestConfigurations_ShouldIncludeNpmScripts()
    {
        // Arrange
        var workingDirectory = "P:\\NodeApp";
        var packageJsonPath = "P:\\NodeApp\\package.json";
        var projectType = ProjectType.GetDefaults().First(p => p.Id == "nodejs-npm");

        _mockFileSystem.Setup(fs => fs.FileExists(packageJsonPath)).Returns(true);
        // Correctly escaped JSON string using standard string literal
        _mockFileSystem.Setup(fs => fs.ReadAllText(packageJsonPath))
            .Returns("{ \"scripts\": { \"dev\": \"vite\", \"build\": \"tsc\" } }");

        // Act
        var configs = _projectDetectionService.SuggestConfigurations(workingDirectory, projectType);

        // Assert
        // npm-dev is skipped because it duplicates the default 'dev' config (npm run dev)
        configs.ShouldContain(c => c.Id == "npm-build" && c.Command == "npm run build");
        configs.ShouldContain(c => c.Id == "dev" && c.IsDefault); // Default dev config
    }

    [Fact]
    public void GetOrCreateConfigurations_ShouldReturnCachedConfigurations_WhenAvailable()
    {
        // Arrange
        var workingDirectory = "P:\\ExistingApp";
        var settings = new DirectorySettings();
        var existingConfig = new RunConfiguration { Id = "custom", Command = "custom-command" };
        settings.RunConfigurations.Add(existingConfig);

        // Act
        var result = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, settings);

        // Assert
        result.ShouldHaveSingleItem();
        result.First().ShouldBe(existingConfig);
        // Should not have triggered detection
        _mockFileSystem.Verify(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()), Times.Never);
    }

    [Fact]
    public void GetOrCreateConfigurations_ShouldDetectAndCache_WhenNoConfigsExist()
    {
        // Arrange
        var workingDirectory = "P:\\NewApp";
        var settings = new DirectorySettings();
        
        // Mock .csproj existence
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Returns(new[] { "P:\\NewApp\\App.csproj" });
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);

        // Act
        var result = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, settings);

        // Assert
        result.ShouldNotBeEmpty();
        settings.DetectedProjectType.ShouldBe("dotnet");
        settings.RunConfigurations.Count.ShouldBe(result.Count);
        settings.ActiveRunConfigurationId.ShouldNotBeNull();
    }

    [Fact]
    public void GetOrCreateConfigurations_ShouldReturnShellConfig_WhenNoProjectDetected()
    {
        // Arrange
        var workingDirectory = "P:\\RandomFolder";
        var settings = new DirectorySettings();
        
        _mockFileSystem.Setup(fs => fs.GetFiles(workingDirectory, It.IsAny<string>(), SearchOption.TopDirectoryOnly))
            .Returns(new string[0]);

        // Act
        var result = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, settings);

        // Assert
        result.ShouldHaveSingleItem();
        result.First().Id.ShouldBe("shell");
        settings.DetectedProjectType.ShouldBeNull(); // Should not set detected type
    }
}

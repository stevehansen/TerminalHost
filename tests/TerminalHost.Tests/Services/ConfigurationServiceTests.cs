using Moq;
using Shouldly;
using TerminalHost.Domain;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class ConfigurationServiceTests
{
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly ConfigurationService _service;
    private readonly string _configPath;

    public ConfigurationServiceTests()
    {
        _fileSystemMock = new Mock<IFileSystem>();
        _service = new ConfigurationService(_fileSystemMock.Object);
        _configPath = ConfigurationService.GetConfigFilePath();
    }

    [Fact]
    public void Load_ShouldReturnDefaultConfig_WhenFileDoesNotExist()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.FileExists(_configPath)).Returns(false);

        // Act
        var config = _service.Load();

        // Assert
        config.ShouldNotBeNull();
        config.Settings.ConfirmOnClose.ShouldBeTrue(); // Default value
        
        // Should also try to save the default
        _fileSystemMock.Verify(x => x.WriteAllText(_configPath, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Load_ShouldReturnDeserializedConfig_WhenFileExists()
    {
        // Arrange
        var json = "{\"settings\": {\"confirmOnClose\": false}}";
        _fileSystemMock.Setup(x => x.FileExists(_configPath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_configPath)).Returns(json);

        // Act
        var config = _service.Load();

        // Assert
        config.ShouldNotBeNull();
        config.Settings.ConfirmOnClose.ShouldBeFalse();
    }

    [Fact]
    public void Save_ShouldWriteToFile()
    {
        // Arrange
        var config = new AppConfiguration
        {
            Settings = new AppSettings { ConfirmOnClose = false }
        };

        // Act
        _service.Save(config);

        // Assert
        _fileSystemMock.Verify(x => x.CreateDirectory(It.IsAny<string>()), Times.Once);
        _fileSystemMock.Verify(x => x.WriteAllText(_configPath, It.Is<string>(s => s.Contains("\"confirmOnClose\": false"))), Times.Once);
    }

    [Fact]
    public void SaveRawJson_ShouldReturnWarning_WhenCustomCommandMissing()
    {
        // Arrange
        var json = @"{
            ""Settings"": {
                ""CustomCommand"": ""invalid_command.exe""
            }
        }";
        
        _fileSystemMock.Setup(x => x.FileExists("invalid_command.exe")).Returns(false);
        // Also need to mock Environment expansion if called, but FileExists returning false handles it generally

        // Act
        var result = _service.SaveRawJson(json);

        // Assert
        result.success.ShouldBeTrue(); // Should still succeed
        result.warning.ShouldNotBeNull();
        result.warning.ShouldContain("Custom command not found");
        
        // Should still save
        _fileSystemMock.Verify(x => x.WriteAllText(_configPath, It.IsAny<string>()), Times.Once);
    }
    
    [Fact]
    public void SaveRawJson_ShouldReturnError_WhenJsonIsInvalid()
    {
        // Arrange
        var json = "{ invalid json }";

        // Act
        var result = _service.SaveRawJson(json);

        // Assert
        result.success.ShouldBeFalse();
        result.error.ShouldContain("JSON Error");
        _fileSystemMock.Verify(x => x.WriteAllText(_configPath, It.IsAny<string>()), Times.Never);
    }
}

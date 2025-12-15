using Moq;
using Shouldly;
using System.IO;
using System.Text.Json;
using Xunit;
using TerminalHost.Domain; // Assuming IFileSystem is here
using TerminalHost.Services;

namespace TerminalHost.Tests.Services;

// A simple test class to use with JsonFileService
public class TestData
{
    public string? Name { get; set; }
    public int Value { get; set; }
    public bool IsActive { get; set; } = true;
}

public class JsonFileServiceTests
{
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly string _testFilePath = "test_dir/test.json";
    private readonly string _testBackupFilePath; // Will be initialized in constructor

    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public JsonFileServiceTests()
    {
        _fileSystemMock = new Mock<IFileSystem>();
        _testBackupFilePath = _testFilePath + ".bak"; // Initialize here
    }

    private JsonFileService<TestData> CreateService()
    {
        return new JsonFileService<TestData>(_fileSystemMock.Object, _testFilePath, _jsonOptions);
    }

    [Fact]
    public void Load_ShouldReturnDefault_WhenNoFilesExist()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(false);
        _fileSystemMock.Setup(x => x.FileExists(_testBackupFilePath)).Returns(false);

        var service = CreateService();

        // Act
        var data = service.Load();

        // Assert
        data.ShouldNotBeNull();
        data.Name.ShouldBeNull();
        data.Value.ShouldBe(0);
        data.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Load_ShouldLoadFromPrimaryFile_WhenValid()
    {
        // Arrange
        var json = "{\"Name\":\"Primary\",\"Value\":1,\"IsActive\":false}";
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testFilePath)).Returns(json);
        _fileSystemMock.Setup(x => x.FileExists(_testBackupFilePath)).Returns(false); // Ensure backup doesn't interfere

        var service = CreateService();

        // Act
        var data = service.Load();

        // Assert
        data.ShouldNotBeNull();
        data.Name.ShouldBe("Primary");
        data.Value.ShouldBe(1);
        data.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Load_ShouldLoadFromBackup_WhenPrimaryIsCorrupted()
    {
        // Arrange
        var corruptedJson = "{invalid json";
        var backupJson = "{\"Name\":\"Backup\",\"Value\":2,\"IsActive\":true}";
        var expectedHealedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<TestData>(backupJson, _jsonOptions), _jsonOptions);

        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testFilePath)).Returns(corruptedJson);
        _fileSystemMock.Setup(x => x.FileExists(_testBackupFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testBackupFilePath)).Returns(backupJson);
        
        // Mock setups for the healing process
        _fileSystemMock.Setup(x => x.CreateDirectory("test_dir"));
        _fileSystemMock.Setup(x => x.WriteAllText(_testFilePath, expectedHealedJson));

        var service = CreateService();

        // Act
        var data = service.Load();

        // Assert
        data.ShouldNotBeNull();
        data.Name.ShouldBe("Backup");
        data.Value.ShouldBe(2);
        data.IsActive.ShouldBeTrue();
        
        // Verify that the service attempted to heal the primary file by saving the backup content
        _fileSystemMock.Verify(x => x.WriteAllText(_testFilePath, expectedHealedJson), Times.Once);
    }

    [Fact]
    public void Load_ShouldLoadFromBackup_WhenPrimaryDoesNotExist()
    {
        // Arrange
        var backupJson = "{\"Name\":\"BackupOnly\",\"Value\":3,\"IsActive\":true}";
        var expectedHealedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<TestData>(backupJson, _jsonOptions), _jsonOptions);
        
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(false);
        _fileSystemMock.Setup(x => x.FileExists(_testBackupFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testBackupFilePath)).Returns(backupJson);

        // Mock setups for the healing process
        _fileSystemMock.Setup(x => x.CreateDirectory("test_dir"));
        _fileSystemMock.Setup(x => x.WriteAllText(_testFilePath, expectedHealedJson));

        var service = CreateService();

        // Act
        var data = service.Load();

        // Assert
        data.ShouldNotBeNull();
        data.Name.ShouldBe("BackupOnly");
        data.Value.ShouldBe(3);
        data.IsActive.ShouldBeTrue();

        // Verify that the service attempted to heal the primary file by saving the backup content
        _fileSystemMock.Verify(x => x.WriteAllText(_testFilePath, expectedHealedJson), Times.Once);
    }

    [Fact]
    public void Load_ShouldReturnDefault_WhenBothFilesCorrupted()
    {
        // Arrange
        var corruptedJson = "{invalid json";
        
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testFilePath)).Returns(corruptedJson);
        _fileSystemMock.Setup(x => x.FileExists(_testBackupFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testBackupFilePath)).Returns(corruptedJson);

        var service = CreateService();

        // Act
        var data = service.Load();

        // Assert
        data.ShouldNotBeNull();
        data.Name.ShouldBeNull();
        data.Value.ShouldBe(0);
        data.IsActive.ShouldBeTrue();
        
        // Verify that no save was attempted since both were corrupted
        _fileSystemMock.Verify(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Save_ShouldWriteToPrimaryFile_WhenNoFilesExist()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(false);
        var dataToSave = new TestData { Name = "NewData", Value = 10 };
        var service = CreateService();

        // Act
        service.Save(dataToSave);

        // Assert
        _fileSystemMock.Verify(x => x.CreateDirectory("test_dir"), Times.Once);
        
        var expectedJson = JsonSerializer.Serialize(dataToSave, _jsonOptions);
        _fileSystemMock.Verify(x => x.WriteAllText(_testFilePath, expectedJson), Times.Once);
        _fileSystemMock.Verify(x => x.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never); // No backup if primary doesn't exist
    }

    [Fact]
    public void Save_ShouldCreateBackupAndWriteToPrimary_WhenPrimaryExists()
    {
        // Arrange
        var existingJson = "{\"Name\":\"OldData\",\"Value\":5,\"IsActive\":false}";
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.ReadAllText(_testFilePath)).Returns(existingJson); // Simulate reading for copy
        _fileSystemMock.Setup(x => x.CreateDirectory("test_dir")); // Setup for CreateDirectory
        
        var dataToSave = new TestData { Name = "UpdatedData", Value = 20 };
        var service = CreateService();

        // Act
        service.Save(dataToSave);

        // Assert
        var expectedJson = JsonSerializer.Serialize(dataToSave, _jsonOptions);
        _fileSystemMock.Verify(x => x.CopyFile(_testFilePath, _testBackupFilePath, true), Times.Once);
        _fileSystemMock.Verify(x => x.WriteAllText(_testFilePath, expectedJson), Times.Once);
    }
    
    [Fact]
    public void Save_ShouldHandleIOExceptionDuringBackupGracefully()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(true);
        _fileSystemMock.Setup(x => x.CopyFile(_testFilePath, _testBackupFilePath, true)).Throws<IOException>(); // Simulate backup failure
        _fileSystemMock.Setup(x => x.CreateDirectory("test_dir")); // Setup for CreateDirectory
        
        var dataToSave = new TestData { Name = "Data", Value = 1 };
        var service = CreateService();

        // Act
        service.Save(dataToSave); // Should not throw

        // Assert (should still try to save primary)
        _fileSystemMock.Verify(x => x.WriteAllText(_testFilePath, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Save_ShouldThrowException_WhenPrimaryWriteFails()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.FileExists(_testFilePath)).Returns(false);
        _fileSystemMock.Setup(x => x.CreateDirectory("test_dir")).Callback(() => {}); // Ensure dir creation doesn't interfere
        _fileSystemMock.Setup(x => x.WriteAllText(_testFilePath, It.IsAny<string>())).Throws<IOException>(); // Simulate primary write failure
        
        var dataToSave = new TestData { Name = "FailingWrite", Value = 99 };
        var service = CreateService();

        // Act & Assert
        Should.Throw<IOException>(() => service.Save(dataToSave));
    }
}
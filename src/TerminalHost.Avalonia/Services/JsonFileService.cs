using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain; // For IFileSystem

namespace TerminalHost.Services;

public class JsonFileService<T> where T : new()
{
    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly string _backupFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonFileService(IFileSystem fileSystem, string filePath, JsonSerializerOptions jsonOptions)
    {
        _fileSystem = fileSystem;
        _filePath = filePath;
        _backupFilePath = filePath + ".bak";
        _jsonOptions = new JsonSerializerOptions(jsonOptions) // Create a copy to add PropertyNameCaseInsensitive
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public T Load()
    {
        T? data = default;
        bool loadedFromBackup = false;

        // Try loading primary file
        if (_fileSystem.FileExists(_filePath))
        {
            try
            {
                var json = _fileSystem.ReadAllText(_filePath);
                data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                if (data != null)
                {
                    return data;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        // Try loading backup file if primary failed or didn't exist
        if (_fileSystem.FileExists(_backupFilePath))
        {
            try
            {
                var json = _fileSystem.ReadAllText(_backupFilePath);
                data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                if (data != null)
                {
                    loadedFromBackup = true; // Mark that we loaded from backup
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }
        
        // If loaded from backup and successfully deserialized, attempt to save it back to primary to "heal" it.
        // This should only happen if 'data' is not null and 'loadedFromBackup' is true.
        if (loadedFromBackup && data != null)
        {
            try
            {
                // Ensure the directory exists before saving
                var directory = Path.GetDirectoryName(_filePath);
                if (directory != null)
                {
                    _fileSystem.CreateDirectory(directory);
                }
                Save(data); // Save the successfully loaded backup to the primary file
            }
            catch (Exception)
            {
            }
        }

        // Return the successfully loaded data, or a new default instance if all attempts failed.
        // The 'data' variable will hold the successfully deserialized object if any of the above
        // loading attempts were successful. Otherwise, it will be null.
        return data ?? new T();
    }

    public void Save(T data)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory != null)
        {
            _fileSystem.CreateDirectory(directory); // Ensure directory exists
        }

        if (_fileSystem.FileExists(_filePath))
        {
            try
            {
                // Copy existing primary to backup before overwriting
                _fileSystem.CopyFile(_filePath, _backupFilePath, true);
            }
            catch (IOException)
            {
                // Continue saving the primary even if backup fails
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            _fileSystem.WriteAllText(_filePath, json);
        }
        catch (IOException)
        {
            throw; // Re-throw to indicate save failure
        }
        catch (JsonException)
        {
            throw; // Re-throw to indicate save failure
        }
    }
}
using System.IO;

namespace TerminalHost.Core.Interfaces;

public interface IFileSystem
{
    bool DirectoryExists(string? path);
    void CreateDirectory(string path);
    bool FileExists(string? path);
    string ReadAllText(string path);
    Task<string> ReadAllTextAsync(string path);
    void WriteAllText(string path, string contents);
    string[] GetFiles(string path, string searchPattern, SearchOption searchOption);
    string[] GetFiles(string path);
    string[] GetDirectories(string path);
    Stream OpenRead(string path);
    long GetFileSize(string path);
    bool IsReadOnly(string path);
    void CopyFile(string sourceFileName, string destFileName, bool overwrite);

    // File explorer operations
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    void Move(string sourcePath, string destPath);
    FileAttributes GetAttributes(string path);
    DateTime GetLastWriteTime(string path);
    bool IsHidden(string path);
}

using System.IO;

namespace TerminalHost.Services;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
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

internal sealed class FileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);
    public string[] GetFiles(string path) => Directory.GetFiles(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public long GetFileSize(string path) => new FileInfo(path).Length;
    public bool IsReadOnly(string path) => new FileInfo(path).IsReadOnly;
    public void CopyFile(string sourceFileName, string destFileName, bool overwrite) => File.Copy(sourceFileName, destFileName, overwrite);

    // File explorer operations
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public void Move(string sourcePath, string destPath)
    {
        if (File.Exists(sourcePath))
            File.Move(sourcePath, destPath);
        else if (Directory.Exists(sourcePath))
            Directory.Move(sourcePath, destPath);
    }
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);
    public bool IsHidden(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    }
}

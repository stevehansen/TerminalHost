using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class FileSystem : IFileSystem
{
    public bool DirectoryExists(string? path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);
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

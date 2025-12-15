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
    public bool DirectoryExists(string path) => System.IO.Directory.Exists(path);
    public void CreateDirectory(string path) => System.IO.Directory.CreateDirectory(path);
    public bool FileExists(string path) => System.IO.File.Exists(path);
    public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
    public Task<string> ReadAllTextAsync(string path) => System.IO.File.ReadAllTextAsync(path);
    public void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => System.IO.Directory.GetFiles(path, searchPattern, searchOption);
    public string[] GetFiles(string path) => System.IO.Directory.GetFiles(path);
    public string[] GetDirectories(string path) => System.IO.Directory.GetDirectories(path);
    public Stream OpenRead(string path) => System.IO.File.OpenRead(path);
    public long GetFileSize(string path) => new System.IO.FileInfo(path).Length;
    public bool IsReadOnly(string path) => new System.IO.FileInfo(path).IsReadOnly;
    public void CopyFile(string sourceFileName, string destFileName, bool overwrite) => System.IO.File.Copy(sourceFileName, destFileName, overwrite);

    // File explorer operations
    public void DeleteFile(string path) => System.IO.File.Delete(path);
    public void DeleteDirectory(string path, bool recursive) => System.IO.Directory.Delete(path, recursive);
    public void Move(string sourcePath, string destPath)
    {
        if (System.IO.File.Exists(sourcePath))
            System.IO.File.Move(sourcePath, destPath);
        else if (System.IO.Directory.Exists(sourcePath))
            System.IO.Directory.Move(sourcePath, destPath);
    }
    public FileAttributes GetAttributes(string path) => System.IO.File.GetAttributes(path);
    public DateTime GetLastWriteTime(string path) => System.IO.File.GetLastWriteTime(path);
    public bool IsHidden(string path)
    {
        var attributes = System.IO.File.GetAttributes(path);
        return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    }
}

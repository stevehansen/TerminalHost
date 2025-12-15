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
    string[] GetDirectories(string path);
    Stream OpenRead(string path);
    long GetFileSize(string path);
    bool IsReadOnly(string path);
    void CopyFile(string sourceFileName, string destFileName, bool overwrite);
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
    public string[] GetDirectories(string path) => System.IO.Directory.GetDirectories(path);
    public Stream OpenRead(string path) => System.IO.File.OpenRead(path);
    public long GetFileSize(string path) => new System.IO.FileInfo(path).Length;
    public bool IsReadOnly(string path) => new System.IO.FileInfo(path).IsReadOnly;
    public void CopyFile(string sourceFileName, string destFileName, bool overwrite) => System.IO.File.Copy(sourceFileName, destFileName, overwrite);
}

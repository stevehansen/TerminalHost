using System.Text;

namespace TerminalHost.Services;

public interface IFileEditService
{
    FileEditResult LoadFile(string filePath);
    FileSaveResult SaveFile(string filePath, string content, Encoding? encoding = null);
    FileEditResult ReloadFile(string filePath);
}

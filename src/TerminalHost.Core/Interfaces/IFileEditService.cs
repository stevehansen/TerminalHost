using System.Text;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IFileEditService
{
    FileEditResult LoadFile(string filePath);
    FileSaveResult SaveFile(string filePath, string content, Encoding? encoding = null);
    FileEditResult ReloadFile(string filePath);
}

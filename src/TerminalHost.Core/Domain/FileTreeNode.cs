namespace TerminalHost.Core.Domain;

public class FileTreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public GitFileStatus? FileStatus { get; set; }
    public List<FileTreeNode> Children { get; set; } = [];
    public bool IsExpanded { get; set; } = true;
    public int FileCount { get; set; }

    public string DisplayIcon => IsFolder ? "\U0001f4c1" : (FileStatus?.StatusIcon ?? "?");
    public string StatusColor => FileStatus?.StatusColor ?? "#CCCCCC";
    public string CountDisplay => IsFolder ? $"({FileCount})" : "";
    public string DisplayName => IsFolder ? Name : (FileStatus?.FileName ?? Name);
}

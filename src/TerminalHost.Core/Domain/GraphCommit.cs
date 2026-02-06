namespace TerminalHost.Core.Domain;

public class GraphColumn
{
    public int Column { get; set; }
    public string Color { get; set; } = "";
}

public class GraphNode
{
    public string Hash { get; set; } = "";
    public int Column { get; set; }
    public string Color { get; set; } = "";
    public List<GraphEdge> Edges { get; set; } = [];
}

public class GraphEdge
{
    public int FromColumn { get; set; }
    public int ToColumn { get; set; }
    public string Color { get; set; } = "";
    public bool IsMerge { get; set; }
}

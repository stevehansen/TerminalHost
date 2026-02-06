using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface ICommitGraphService
{
    List<GraphNode> ComputeGraph(List<GitCommit> commits);
}

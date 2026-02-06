using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public class CommitGraphService : ICommitGraphService
{
    private static readonly string[] LaneColors =
    [
        "#569CD6", // Blue
        "#4EC9B0", // Teal
        "#CE9178", // Orange
        "#C586C0", // Purple
        "#DCDCAA", // Yellow
        "#9CDCFE", // Light blue
        "#D7BA7D", // Gold
        "#F14C4C", // Red
        "#22C55E", // Green
        "#E2C08D"  // Amber
    ];

    public List<GraphNode> ComputeGraph(List<GitCommit> commits)
    {
        var nodes = new List<GraphNode>();
        if (commits.Count == 0) return nodes;

        // Map hash → index for quick lookups
        var hashToIndex = new Dictionary<string, int>();
        for (int i = 0; i < commits.Count; i++)
        {
            if (!string.IsNullOrEmpty(commits[i].Hash))
                hashToIndex[commits[i].Hash] = i;
        }

        // Active lanes: each slot holds the hash of the commit that "owns" that lane
        var lanes = new List<string?>();
        var laneColors = new Dictionary<string, string>();
        int colorIndex = 0;

        for (int i = 0; i < commits.Count; i++)
        {
            var commit = commits[i];
            var hash = commit.Hash ?? "";
            var parents = commit.ParentHashes ?? [];

            // Skip WIP entries
            if (commit.IsWipEntry)
            {
                nodes.Add(new GraphNode { Hash = hash, Column = 0, Color = "#E2C08D" });
                continue;
            }

            // Find this commit's lane (it should be in an existing lane if a prior commit referenced it)
            int column = lanes.IndexOf(hash);
            if (column == -1)
            {
                // New lane - find first empty slot or append
                column = lanes.IndexOf(null);
                if (column == -1)
                {
                    column = lanes.Count;
                    lanes.Add(hash);
                }
                else
                {
                    lanes[column] = hash;
                }
            }

            // Assign color
            if (!laneColors.ContainsKey(hash))
            {
                laneColors[hash] = LaneColors[colorIndex % LaneColors.Length];
                colorIndex++;
            }
            var color = laneColors[hash];

            var node = new GraphNode
            {
                Hash = hash,
                Column = column,
                Color = color,
                Edges = []
            };

            // Process parents
            if (parents.Count > 0)
            {
                // First parent continues in the same lane
                var firstParent = parents[0];
                int firstParentLane = lanes.IndexOf(firstParent);
                if (firstParentLane == -1)
                {
                    // Parent takes over this lane
                    lanes[column] = firstParent;
                    firstParentLane = column;
                    if (!laneColors.ContainsKey(firstParent))
                        laneColors[firstParent] = color;
                }
                else if (firstParentLane != column)
                {
                    // Lane merges - free current lane
                    lanes[column] = null;
                }

                node.Edges.Add(new GraphEdge
                {
                    FromColumn = column,
                    ToColumn = firstParentLane,
                    Color = color,
                    IsMerge = false
                });

                // Additional parents branch into new lanes
                for (int p = 1; p < parents.Count; p++)
                {
                    var parent = parents[p];
                    int parentLane = lanes.IndexOf(parent);
                    if (parentLane == -1)
                    {
                        // Find empty slot or append
                        parentLane = lanes.IndexOf(null);
                        if (parentLane == -1)
                        {
                            parentLane = lanes.Count;
                            lanes.Add(parent);
                        }
                        else
                        {
                            lanes[parentLane] = parent;
                        }
                        if (!laneColors.ContainsKey(parent))
                        {
                            laneColors[parent] = LaneColors[colorIndex % LaneColors.Length];
                            colorIndex++;
                        }
                    }

                    node.Edges.Add(new GraphEdge
                    {
                        FromColumn = column,
                        ToColumn = parentLane,
                        Color = laneColors[parent],
                        IsMerge = true
                    });
                }
            }
            else
            {
                // Root commit - free the lane
                lanes[column] = null;
            }

            // Clean up trailing null lanes
            while (lanes.Count > 0 && lanes[^1] == null)
                lanes.RemoveAt(lanes.Count - 1);

            nodes.Add(node);
        }

        return nodes;
    }
}

using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>Memory entry type.</summary>
public enum MemoryType { Observation, Insight, Procedure, Heuristic }

/// <summary>How the memory was created.</summary>
public enum MemoryProvenance { UserStated, AgentInferred, ToolOutput, Consolidation, Intake, Bundle, System }

/// <summary>Memory layer type.</summary>
public enum LayerType { Local, Shared, Base }

// --- Slim DTOs for Eidet REST API responses ---

/// <summary>Response from GET /api/health (fast, no DB/Ollama round-trip).</summary>
public class EidetHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>Whether the service is healthy (status == "ok").</summary>
    public bool IsHealthy => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Response from GET /api/status (full, includes DB + Ollama).</summary>
public class EidetStatusResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("database")]
    public EidetDatabaseInfo? Database { get; set; }

    /// <summary>Whether the service is running (status == "running").</summary>
    public bool IsRunning => string.Equals(Status, "running", StringComparison.OrdinalIgnoreCase);

    /// <summary>Document count from the database info.</summary>
    public int DocumentCount => Database?.DocumentCount ?? 0;
}

/// <summary>Database info nested in status response.</summary>
public class EidetDatabaseInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("documentCount")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("indexExists")]
    public bool IndexExists { get; set; }
}

/// <summary>A memory entry returned from Eidet search/get endpoints.</summary>
public class EidetMemoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("repoId")]
    public string RepoId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("oneLiner")]
    public string? OneLiner { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = [];

    [JsonPropertyName("importance")]
    public float Importance { get; set; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("accessCount")]
    public int AccessCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastAccessedAt")]
    public DateTime? LastAccessedAt { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("provenance")]
    public string Provenance { get; set; } = "";

    [JsonPropertyName("foresightHint")]
    public string? ForesightHint { get; set; }

    [JsonPropertyName("layerId")]
    public string? LayerId { get; set; }

    [JsonPropertyName("validUntil")]
    public DateTime? ValidUntil { get; set; }

    /// <summary>Parse the type string into the MemoryType enum.</summary>
    public MemoryType ParsedType => Enum.TryParse<MemoryType>(Type, ignoreCase: true, out var t) ? t : MemoryType.Observation;

    /// <summary>Parse the provenance string into the MemoryProvenance enum.</summary>
    public MemoryProvenance ParsedProvenance => Enum.TryParse<MemoryProvenance>(Provenance, ignoreCase: true, out var p) ? p : MemoryProvenance.AgentInferred;
}

/// <summary>A single search result with score.</summary>
public class EidetSearchResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("repoId")]
    public string RepoId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("oneLiner")]
    public string? OneLiner { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = [];

    [JsonPropertyName("importance")]
    public float Importance { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("layerSource")]
    public string? LayerSource { get; set; }
}

/// <summary>Wrapper for search endpoint response.</summary>
public class EidetSearchResponse
{
    [JsonPropertyName("results")]
    public List<EidetSearchResult> Results { get; set; } = [];
}

/// <summary>Response from GET /api/eidet/stats — text summary + counts by lowercase type name.</summary>
public class EidetStatsResponse
{
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>A memory layer from Eidet.</summary>
public class EidetLayerInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    /// <summary>Parse the type string into the LayerType enum.</summary>
    public LayerType ParsedType => Enum.TryParse<LayerType>(Type, ignoreCase: true, out var t) ? t : LayerType.Local;
}

/// <summary>Wrapper for layers endpoint response.</summary>
public class EidetLayersResponse
{
    [JsonPropertyName("layers")]
    public List<EidetLayerInfo> Layers { get; set; } = [];
}

/// <summary>Response from POST /api/eidet/intake.</summary>
public class EidetIntakeResponse
{
    [JsonPropertyName("newCount")]
    public int NewCount { get; set; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    [JsonPropertyName("dependencies")]
    public int Dependencies { get; set; }
}

/// <summary>Wrapper for /api/eidet/browse response.</summary>
public class EidetMemoriesResponse
{
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("entries")]
    public List<EidetMemoryEntry> Entries { get; set; } = [];
}

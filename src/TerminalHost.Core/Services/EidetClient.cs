using System.Net.Http;
using System.Text;
using System.Text.Json;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

/// <summary>
/// Slim HttpClient wrapper for Eidet's REST API.
/// All memory complexity lives in the Eidet service — this is just HTTP plumbing.
/// </summary>
public class EidetClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public EidetClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>Fast health check — GET /api/health (no DB/Ollama round-trip).</summary>
    public async Task<EidetHealthResponse?> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/health", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<EidetHealthResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full status — GET /api/status (includes DB + Ollama info).</summary>
    public async Task<EidetStatusResponse?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/status", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<EidetStatusResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Trigger intake for a project — POST /api/eidet/intake.</summary>
    public async Task<EidetIntakeResponse?> IntakeAsync(string projectPath, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { projectPath }, JsonOptions);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/api/eidet/intake", content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetIntakeResponse>(json, JsonOptions);
    }

    /// <summary>Get stats — GET /api/eidet/stats?repo=...</summary>
    /// <remarks>
    /// Eidet returns a text blurb ({ repo, summary }), not structured counts.
    /// For structured counts callers should browse by type or use the quality endpoint.
    /// </remarks>
    public async Task<EidetStatsResponse?> GetStatsAsync(string repoId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/eidet/stats?repo={Uri.EscapeDataString(repoId)}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetStatsResponse>(json, JsonOptions);
    }

    /// <summary>Search memories — GET /api/eidet/search?repo=...&amp;q=...</summary>
    public async Task<EidetSearchResponse?> SearchAsync(string repoId, string query, string? type = null, int limit = 100, CancellationToken ct = default)
    {
        var url = $"/api/eidet/search?repo={Uri.EscapeDataString(repoId)}&q={Uri.EscapeDataString(query)}&limit={limit}";
        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetSearchResponse>(json, JsonOptions);
    }

    /// <summary>Get context — GET /api/eidet/context?repo=...</summary>
    public async Task<string> GetContextAsync(string repoId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/eidet/context?repo={Uri.EscapeDataString(repoId)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>List layers — GET /api/eidet/layers?repo=...</summary>
    public async Task<EidetLayersResponse?> GetLayersAsync(string repoId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/eidet/layers?repo={Uri.EscapeDataString(repoId)}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetLayersResponse>(json, JsonOptions);
    }

    /// <summary>Browse memories for a repo — GET /api/eidet/browse?repo=...&amp;type=...</summary>
    public async Task<EidetMemoriesResponse?> GetMemoriesAsync(string repoId, string? type = null, CancellationToken ct = default)
    {
        var url = $"/api/eidet/browse?repo={Uri.EscapeDataString(repoId)}&take=200";
        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetMemoriesResponse>(json, JsonOptions);
    }

    /// <summary>Get memory by ID — GET /api/eidet/{id}.</summary>
    public async Task<EidetMemoryEntry?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/eidet/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetMemoryEntry>(json, JsonOptions);
    }

    /// <summary>Forget memory — DELETE /api/eidet/{id}.</summary>
    public async Task<bool> ForgetAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/eidet/{Uri.EscapeDataString(id)}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Trigger maintenance — POST /api/maintenance.</summary>
    public async Task<string> RunMaintenanceAsync(string? repoId = null, CancellationToken ct = default)
    {
        var url = "/api/maintenance";
        if (!string.IsNullOrEmpty(repoId))
            url += $"?repo={Uri.EscapeDataString(repoId)}";
        var response = await _http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Export memories — GET /api/eidet/export?repo=...</summary>
    public async Task<string> ExportAsync(string repoId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/eidet/export?repo={Uri.EscapeDataString(repoId)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Raw proxy: forward a GET request path+query to Eidet, return the response body.
    /// Used by ApiServer thin proxy endpoints.
    /// </summary>
    public async Task<(int StatusCode, string Body, string ContentType)> ProxyGetAsync(string pathAndQuery, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(pathAndQuery, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return ((int)response.StatusCode, body, contentType);
        }
        catch (Exception ex)
        {
            return (502, JsonSerializer.Serialize(new { error = ex.Message }), "application/json");
        }
    }

    /// <summary>
    /// Raw proxy: forward a POST request to Eidet, return the response body.
    /// </summary>
    public async Task<(int StatusCode, string Body, string ContentType)> ProxyPostAsync(string pathAndQuery, string? requestBody = null, CancellationToken ct = default)
    {
        try
        {
            var content = requestBody != null
                ? new StringContent(requestBody, Encoding.UTF8, "application/json")
                : null;
            var response = await _http.PostAsync(pathAndQuery, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return ((int)response.StatusCode, body, contentType);
        }
        catch (Exception ex)
        {
            return (502, JsonSerializer.Serialize(new { error = ex.Message }), "application/json");
        }
    }

    public void Dispose() => _http.Dispose();
}

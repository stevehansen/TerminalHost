// Copyright (c) TerminalHost. All rights reserved.

using System.Net.Http;
using System.Text.Json;

namespace TerminalHost.CmdPal.Helpers;

/// <summary>
/// HTTP client for the TerminalHost REST API.
/// All methods return null when TerminalHost is not running or the API is disabled.
/// Thread-safe for concurrent use from dock polling and page fetches.
/// </summary>
internal sealed class ApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string _baseUrl = "http://127.0.0.1:19280";

    /// <summary>
    /// Whether the last API call succeeded. Use for quick "is TerminalHost reachable?" checks.
    /// </summary>
    public bool IsAvailable { get; private set; }

    public ApiClient()
    {
        // Try to discover the configured port from TerminalHost's config
        TryDiscoverPort();
    }

    public async Task<ApiModels.StatusResponse?> GetStatusAsync()
    {
        return await GetAsync<ApiModels.StatusResponse>("/api/status");
    }

    public async Task<ApiModels.ReposResponse?> GetReposAsync()
    {
        return await GetAsync<ApiModels.ReposResponse>("/api/repos");
    }

    public async Task<ApiModels.RepoInfo?> GetRepoDetailAsync(int index)
    {
        return await GetAsync<ApiModels.RepoInfo>($"/api/repos/{index}");
    }

    public async Task<ApiModels.GitDetailInfo?> GetRepoGitAsync(int index)
    {
        return await GetAsync<ApiModels.GitDetailInfo>($"/api/repos/{index}/git");
    }

    public async Task<ApiModels.TasksResponse?> GetTasksAsync()
    {
        return await GetAsync<ApiModels.TasksResponse>("/api/tasks");
    }

    public async Task<ApiModels.TimelineResponse?> GetTimelineAsync(int limit = 20)
    {
        return await GetAsync<ApiModels.TimelineResponse>($"/api/timeline?limit={limit}");
    }

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var response = await Http.GetAsync(_baseUrl + path);
            if (!response.IsSuccessStatusCode)
            {
                IsAvailable = false;
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            IsAvailable = true;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (HttpRequestException)
        {
            IsAvailable = false;
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout
            IsAvailable = false;
            return null;
        }
    }

    /// <summary>
    /// Reads TerminalHost's config.json to discover the API port if it's non-default.
    /// </summary>
    private void TryDiscoverPort()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configPath = Path.Combine(appData, "TerminalHost", "config.json");

            if (!File.Exists(configPath))
                return;

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("settings", out var settings) &&
                settings.TryGetProperty("api", out var api) &&
                api.TryGetProperty("port", out var port) &&
                port.TryGetInt32(out var portValue) &&
                portValue != 19280)
            {
                _baseUrl = $"http://127.0.0.1:{portValue}";
            }
        }
        catch
        {
            // Ignore — use default port
        }
    }
}

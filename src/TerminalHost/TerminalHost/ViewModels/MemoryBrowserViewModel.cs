using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// Item displayed in the memory browser list.
/// </summary>
public partial class MemoryBrowserItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string RepoId { get; init; } = "";
    public MemoryType Type { get; init; }
    public string Content { get; init; } = "";
    public string? Summary { get; init; }
    public string? OneLiner { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<string> Entities { get; init; } = [];
    public float Importance { get; init; }
    public float Confidence { get; init; }
    public int AccessCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public string Source { get; init; } = "";
    public MemoryProvenance Provenance { get; init; }
    public string? ForesightHint { get; init; }
    public string? LayerId { get; init; }
    public DateTime? ValidUntil { get; init; }

    public string TypeIcon => Type switch
    {
        MemoryType.Observation => "👁",
        MemoryType.Insight => "💡",
        MemoryType.Procedure => "📋",
        MemoryType.Heuristic => "⚡",
        _ => "📝"
    };

    public string TypeLabel => Type.ToString();

    /// <summary>Best available compact display: OneLiner > Summary > truncated Content.</summary>
    public string DisplayContent => OneLiner ?? Summary ?? (Content.Length > 200 ? Content[..200] + "..." : Content);

    public string TagsDisplay => Tags.Count > 0 ? string.Join(", ", Tags) : "";

    public string EntitiesDisplay => Entities.Count > 0 ? string.Join(", ", Entities.Take(8)) : "";

    public bool HasEntities => Entities.Count > 0;

    public bool HasForesight => !string.IsNullOrEmpty(ForesightHint);

    public string ImportanceDisplay => $"{Importance:P0}";

    public string ConfidenceDisplay => $"{Confidence:P0}";

    public string ProvenanceDisplay => Provenance switch
    {
        MemoryProvenance.UserStated => "👤 User",
        MemoryProvenance.AgentInferred => "🤖 Agent",
        MemoryProvenance.ToolOutput => "🔧 Tool",
        MemoryProvenance.Consolidation => "🔄 Consolidated",
        MemoryProvenance.Intake => "📥 Intake",
        MemoryProvenance.Bundle => "📦 Bundle",
        MemoryProvenance.System => "⚙️ System",
        _ => Source
    };

    public string AgeDisplay
    {
        get
        {
            var age = DateTime.UtcNow - CreatedAt;
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
            if (age.TotalDays < 30) return $"{(int)age.TotalDays}d ago";
            return CreatedAt.ToString("yyyy-MM-dd");
        }
    }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Layer stack item for the layer visualization.
/// </summary>
public class LayerStackItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public LayerType Type { get; init; }
    public bool ReadOnly { get; init; }
    public int Priority { get; init; }
    public int EntryCount { get; set; }
    public string TypeIcon => Type switch
    {
        LayerType.Local => "📝",
        LayerType.Shared => "👥",
        LayerType.Base => "📦",
        _ => "📄"
    };
}

/// <summary>
/// ViewModel for the Memory Browser center panel.
/// Shows memories for the current repo via Eidet REST API.
/// </summary>
public partial class MemoryBrowserViewModel : BasePanelViewModel
{
    private readonly IToastService _toastService;
    private readonly IDispatcherService _dispatcherService;
    private TerminalPairTabViewModel? _currentTab;

    public override string PanelId => "memoryBrowser";
    public override string PanelTitle => "Memory Browser";
    public override string PanelIcon => "🧠";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #region State Properties

    [ObservableProperty]
    private string _title = "Memory Browser";

    [ObservableProperty]
    private string _repoId = "";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedTypeFilter = "All";

    [ObservableProperty]
    private string _selectedSourceFilter = "All";

    [ObservableProperty]
    private bool _showExpired;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _observationCount;

    [ObservableProperty]
    private int _insightCount;

    [ObservableProperty]
    private int _procedureCount;

    [ObservableProperty]
    private int _heuristicCount;

    [ObservableProperty]
    private MemoryBrowserItem? _selectedItem;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    #endregion

    #region Collections

    public ObservableCollection<MemoryBrowserItem> Memories { get; } = [];
    public ObservableCollection<LayerStackItem> Layers { get; } = [];

    public List<string> TypeFilters { get; } = ["All", "Observation", "Insight", "Procedure", "Heuristic"];
    public List<string> SourceFilters { get; } = ["All", "intake", "claude-session", "consolidation", "user", "system"];

    #endregion

    public MemoryBrowserViewModel(
        IToastService toastService,
        IDispatcherService dispatcherService)
    {
        _toastService = toastService;
        _dispatcherService = dispatcherService;
        DisplayState = PanelDisplayState.Panel;
        Width = 900;
        Height = 600;
    }

    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTab = terminalTab;
        var workDir = terminalTab.Pair.WorkingDirectory;
        RepoId = RepoIdNormalizer.Normalize(workDir);
        Title = $"Memory Browser — {Path.GetFileName(workDir)}";
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var eidet = App.Current.Services.GetService<EidetClientService>();
        if (eidet is null || !eidet.IsConnected || eidet.Client is null)
        {
            ConnectionStatus = "Disconnected";
            Memories.Clear();
            Layers.Clear();
            return;
        }

        ConnectionStatus = "Connected";
        IsLoading = true;

        try
        {
            var client = eidet.Client;

            // Load stats
            var stats = await client.GetStatsAsync(RepoId);
            if (stats != null)
            {
                ObservationCount = stats.Counts.GetValueOrDefault("observation");
                InsightCount = stats.Counts.GetValueOrDefault("insight");
                ProcedureCount = stats.Counts.GetValueOrDefault("procedure");
                HeuristicCount = stats.Counts.GetValueOrDefault("heuristic");
                TotalCount = stats.Total;
            }

            // Type filter
            var typeFilter = SelectedTypeFilter switch
            {
                "Observation" => "observation",
                "Insight" => "insight",
                "Procedure" => "procedure",
                "Heuristic" => "heuristic",
                _ => (string?)null
            };

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                // Search via Eidet
                var searchResponse = await client.SearchAsync(RepoId, SearchText, typeFilter, 100);

                _dispatcherService.Invoke(() =>
                {
                    Memories.Clear();
                    if (searchResponse?.Results != null)
                    {
                        foreach (var r in searchResponse.Results)
                        {
                            Memories.Add(new MemoryBrowserItem
                            {
                                Id = r.Id,
                                RepoId = r.RepoId,
                                Type = Enum.TryParse<MemoryType>(r.Type, ignoreCase: true, out var t) ? t : MemoryType.Observation,
                                Content = r.Content,
                                Summary = r.Summary,
                                OneLiner = r.OneLiner,
                                Tags = r.Tags,
                                Entities = r.Entities,
                                Importance = r.Importance,
                                CreatedAt = r.CreatedAt,
                                LayerId = r.LayerSource,
                            });
                        }
                    }
                });
            }
            else
            {
                // Browse all memories via Eidet
                var memoriesResponse = await client.GetMemoriesAsync(RepoId, typeFilter);

                _dispatcherService.Invoke(() =>
                {
                    Memories.Clear();
                    if (memoriesResponse?.Entries != null)
                    {
                        foreach (var e in memoriesResponse.Entries.OrderByDescending(e => e.CreatedAt))
                        {
                            Memories.Add(new MemoryBrowserItem
                            {
                                Id = e.Id,
                                RepoId = e.RepoId,
                                Type = e.ParsedType,
                                Content = e.Content,
                                Summary = e.Summary,
                                OneLiner = e.OneLiner,
                                Tags = e.Tags,
                                Entities = e.Entities,
                                Importance = e.Importance,
                                Confidence = e.Confidence,
                                AccessCount = e.AccessCount,
                                CreatedAt = e.CreatedAt,
                                LastAccessedAt = e.LastAccessedAt,
                                Source = e.Source,
                                Provenance = e.ParsedProvenance,
                                ForesightHint = e.ForesightHint,
                                LayerId = e.LayerId,
                                ValidUntil = e.ValidUntil,
                            });
                        }
                    }
                });
            }

            // Load layer stack
            var layersResponse = await client.GetLayersAsync(RepoId);
            _dispatcherService.Invoke(() =>
            {
                Layers.Clear();
                // Local layer first
                Layers.Add(new LayerStackItem
                {
                    Id = RepoId,
                    Name = "Local",
                    Type = LayerType.Local,
                    ReadOnly = false,
                    Priority = 100,
                    EntryCount = TotalCount,
                });
                if (layersResponse?.Layers != null)
                {
                    foreach (var layer in layersResponse.Layers)
                    {
                        Layers.Add(new LayerStackItem
                        {
                            Id = layer.Id,
                            Name = layer.Name,
                            Type = layer.ParsedType,
                            ReadOnly = layer.ReadOnly,
                            Priority = layer.Priority,
                            EntryCount = layer.EntryCount,
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _toastService.Show($"Memory browser error: {ex.Message}", ToastType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ForgetMemoryAsync()
    {
        if (SelectedItem is null) return;

        var eidet = App.Current.Services.GetService<EidetClientService>();
        if (eidet?.Client is null) return;

        try
        {
            var forgotten = await eidet.Client.ForgetAsync(SelectedItem.Id);
            if (forgotten)
            {
                _toastService.Show($"Memory forgotten: {SelectedItem.Id}", ToastType.Success);
                _dispatcherService.Invoke(() => Memories.Remove(SelectedItem));
                SelectedItem = null;
                // Refresh stats
                var stats = await eidet.Client.GetStatsAsync(RepoId);
                if (stats != null)
                {
                    ObservationCount = stats.Counts.GetValueOrDefault("observation");
                    InsightCount = stats.Counts.GetValueOrDefault("insight");
                    ProcedureCount = stats.Counts.GetValueOrDefault("procedure");
                    HeuristicCount = stats.Counts.GetValueOrDefault("heuristic");
                    TotalCount = stats.Total;
                }
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"Forget failed: {ex.Message}", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task RunIntakeAsync()
    {
        if (_currentTab is null) return;

        var eidet = App.Current.Services.GetService<EidetClientService>();
        if (eidet is null) return;

        await eidet.RunIntakeAsync(_currentTab.Pair.WorkingDirectory);
        await RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
    partial void OnSelectedTypeFilterChanged(string value) => _ = RefreshAsync();
    partial void OnSelectedSourceFilterChanged(string value) => _ = RefreshAsync();
    partial void OnShowExpiredChanged(bool value) => _ = RefreshAsync();
}

namespace TerminalHost.Core.Domain;

/// <summary>
/// In-memory webhook delivery statistics for diagnostics.
/// </summary>
public class WebhookDeliveryStats
{
    public int TotalDelivered { get; set; }
    public int TotalFailed { get; set; }
    public int PendingRetries { get; set; }
    public Dictionary<string, int> DeliveriesByEndpoint { get; set; } = new();
    public Dictionary<string, int> FailuresByEndpoint { get; set; } = new();
}

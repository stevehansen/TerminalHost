using System;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Manages webhook delivery including debounce, retry, and batching.
/// </summary>
public interface IWebhookDeliveryService : IDisposable
{
    /// <summary>Enqueue an event for delivery to matching webhooks.</summary>
    void Enqueue(ApiEvent apiEvent);

    /// <summary>Get delivery statistics.</summary>
    WebhookDeliveryStats GetStats();

    /// <summary>Send a test event to all enabled webhooks.</summary>
    Task TestWebhooksAsync();
}

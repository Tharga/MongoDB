namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Message sent from a remote agent to the central monitor server
/// containing a completed database call.
/// </summary>
public record MonitorCallMessage
{
    public required CallDto Call { get; init; }
}

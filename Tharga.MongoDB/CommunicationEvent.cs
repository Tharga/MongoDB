using System;

namespace Tharga.MongoDB;

/// <summary>
/// Direction of a monitor message relative to the central server: <see cref="Inbound"/> = received
/// from an agent, <see cref="Outbound"/> = sent to an agent.
/// </summary>
public enum CommunicationDirection
{
    Inbound,
    Outbound,
}

/// <summary>
/// A single recorded SignalR message between the central server and a monitoring agent, for the
/// per-agent Communication view. Diagnostic only — a bounded, in-memory history.
/// </summary>
public record CommunicationEvent
{
    public required DateTime Timestamp { get; init; }
    public required CommunicationDirection Direction { get; init; }
    public required string MessageType { get; init; }
    public string Summary { get; init; }
}

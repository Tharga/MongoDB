using System.Collections.Generic;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// Bounded in-memory record of SignalR messages exchanged with each monitoring agent, keyed by
/// source name. Powers the per-agent Communication diagnostic view.
/// </summary>
internal interface ICommunicationLog
{
    void Record(string sourceName, CommunicationDirection direction, string messageType, string summary);
    IReadOnlyList<CommunicationEvent> Get(string sourceName);
}

using System;

namespace Tharga.MongoDB;

/// <summary>
/// Thrown when the execute limiter refuses an operation instead of queueing it. Catch this to shed or retry
/// load without having to distinguish why; catch the derived types when the reason matters.
/// </summary>
public abstract class ExecuteLimiterException : Exception
{
    protected ExecuteLimiterException(string message, string serverKey)
        : base(message)
    {
        ServerKey = serverKey;
    }

    /// <summary>The connection pool the operation was queued against — the server host(s) plus the pool size.</summary>
    public string ServerKey { get; }
}

/// <summary>
/// Thrown when a connection pool already has <see cref="ExecuteLimiterOptions.MaxQueueLength"/> operations
/// waiting for a slot. The operation never queued, so nothing is holding memory on its behalf.
/// </summary>
public class ExecuteLimiterQueueFullException : ExecuteLimiterException
{
    public ExecuteLimiterQueueFullException(string serverKey, int maxQueueLength)
        : base($"The execute queue for '{serverKey}' is full at its limit of '{maxQueueLength}' waiting operations. Retry later, or raise 'Limiter.MaxQueueLength'.", serverKey)
    {
        MaxQueueLength = maxQueueLength;
    }

    /// <summary>The configured limit that was reached.</summary>
    public int MaxQueueLength { get; }
}

/// <summary>
/// Thrown when an operation waited longer than <see cref="ExecuteLimiterOptions.QueueTimeout"/> for a slot.
/// </summary>
public class ExecuteLimiterQueueTimeoutException : ExecuteLimiterException
{
    public ExecuteLimiterQueueTimeoutException(string serverKey, TimeSpan queueTimeout)
        : base($"Waited '{queueTimeout}' for an execute slot on '{serverKey}' without getting one. Retry later, or raise 'Limiter.QueueTimeout'.", serverKey)
    {
        QueueTimeout = queueTimeout;
    }

    /// <summary>The configured timeout that elapsed.</summary>
    public TimeSpan QueueTimeout { get; }
}

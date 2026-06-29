using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB;

/// <summary>
/// Runtime gate for how much per-call data this process records (see <see cref="CallRecordingLevel"/>).
/// <see cref="CallsConsumed"/> reflects whether anything is currently consuming this process's calls — set true
/// while call forwarding is on or a live viewer is attached (and always on the central monitor server, which
/// consumes its own calls locally). A singleton; read on the call path, so reads must be cheap.
/// </summary>
public sealed class MonitorRecordingState
{
    private volatile bool _callsConsumed;

    /// <summary>The configured recording level (from <c>Monitor.CallRecordingLevel</c>).</summary>
    public CallRecordingLevel Level { get; set; } = CallRecordingLevel.OnDemand;

    /// <summary>Whether something is currently consuming this process's calls (forwarding on, a live viewer, or the server itself).</summary>
    public bool CallsConsumed
    {
        get => _callsConsumed;
        set => _callsConsumed = value;
    }

    /// <summary>Whether a call should be recorded at all right now.</summary>
    public bool ShouldRecord => Level != CallRecordingLevel.WhenConsumed || _callsConsumed;

    /// <summary>Whether the detailed step timeline should be recorded right now (vs. just the lightweight call).</summary>
    public bool ShouldRecordSteps => Level == CallRecordingLevel.Full || _callsConsumed;
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB;

public record CallInfo
{
    public required Guid Key { get; init; }
    public required DateTime StartTime { get; init; }
    public required CollectionFingerprint Fingerprint { get; init; }
    public required string FunctionName { get; init; }
    public required Operation Operation { get; set; }
    public TimeSpan? Elapsed { get; set; }
    public int? Count { get; set; }
    public Exception Exception { get; set; }
    public bool Final { get; set; }
    public IReadOnlyList<CallStepInfo> Steps { get; set; }

    private Lazy<string> _filterJson;
    public string FilterJson => _filterJson?.Value;
    public Func<string> FilterJsonProvider
    {
        set => _filterJson = value != null ? new Lazy<string>(value) : null;
    }

    public Func<CancellationToken, Task<string>> ExplainProvider { get; set; }
}
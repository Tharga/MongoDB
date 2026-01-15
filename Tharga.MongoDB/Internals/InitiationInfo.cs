using System.Collections.Generic;

namespace Tharga.MongoDB.Internals;

internal record InitiationInfo
{
    public bool IndexAssured { get; set; }
    public long? VirtualCount { get; set; }
    public List<(IndexFailOperation Operation, string Name)> FailedIndices { get; set; } = new();
}
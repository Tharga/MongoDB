using System;
using System.Collections.Generic;

namespace Tharga.MongoDB;

internal interface ICallLibrary : IDisposable
{
    event EventHandler CallChanged;
    void StartCall(CallStartEventArgs callStartEventArgs);
    void EndCall(CallEndEventArgs e);
    IEnumerable<CallInfo> GetLastCalls();
    IEnumerable<CallInfo> GetSlowCalls();
    IEnumerable<CallInfo> GetOngoingCalls();
    CallInfo GetCall(Guid key);
    IReadOnlyDictionary<string, int> GetCallCounts();
    IReadOnlyDictionary<string, int> GetCallCountsBySuffix();
    void IngestCall(CallInfo call);
    void ResetCalls();
}
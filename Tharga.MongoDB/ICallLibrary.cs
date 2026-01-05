using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

internal interface ICallLibrary
{
    void StartCall(CallStartEventArgs callStartEventArgs);
    Task<CollectionFingerprint> EndCallAsync(CallEndEventArgs e);
    IEnumerable<CallInfo> GetLastCalls();
    IEnumerable<CallInfo> GetSlowCalls();
    IEnumerable<CallInfo> GetOngoingCalls();
}
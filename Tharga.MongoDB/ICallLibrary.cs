using System.Collections.Generic;

namespace Tharga.MongoDB;

internal interface ICallLibrary
{
    void StartCall(CallStartEventArgs callStartEventArgs);
    void EndCall(CallEndEventArgs callEndEventArgs);
    IEnumerable<CallInfo> GetLastCalls();
    IEnumerable<CallInfo> GetSlowCalls();
}
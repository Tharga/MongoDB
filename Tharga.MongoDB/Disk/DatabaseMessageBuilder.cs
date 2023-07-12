using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB.Disk;

internal static class DatabaseMessageBuilder
{
    public static Dictionary<string, object> BuildData(params (string Key, object Value)[] data)
    {
        var result = new[] { ("action", (object)"Database") }.Union(data).ToDictionary(x => x.Item1, x => x.Item2);
        return result;
    }
}
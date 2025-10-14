//using System.Collections.Generic;
//using System.Linq;

//namespace Tharga.MongoDB.Internals;

//internal static class AggregatePropertyCache
//{
//    private static readonly Dictionary<string, string[]> _aggregatePropertyMapCache = new();

//    public static IEnumerable<string> GetProperties<TSource, TTarget>()
//    {
//        var key = $"{typeof(TTarget).FullName}.{typeof(TSource)}";
//        if (_aggregatePropertyMapCache.TryGetValue(key, out var props)) return props;

//        props = BuildProperties<TSource, TTarget>().ToArray();
//        _aggregatePropertyMapCache.TryAdd(key, props);
//        return props;
//    }

//    private static IEnumerable<string> BuildProperties<TSource, TTarget>()
//    {
//        var sourceProperties = typeof(TSource).GetProperties();
//        var targetProperties = typeof(TTarget).GetProperties();

//        foreach (var sourceProperty in sourceProperties.Where(x => x.Name != "Id"))
//        {
//            var prop = targetProperties.FirstOrDefault(x => x.Name == sourceProperty.Name && x.MemberType == sourceProperty.MemberType);
//            if (prop != null)
//            {
//                yield return prop.Name;
//            }
//        }
//    }
//}
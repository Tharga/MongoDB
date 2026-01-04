using System.Text;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Tharga.MongoDB.Disk;

namespace Tharga.MongoDB.Blazor;

public static class IndexModelExtensions
{
    public static string Diff(this IndexModel[] indicies)
    {
        var sb = new StringBuilder();
        foreach (var index in indicies)
        {
            sb.Append($"{index.Name}: ");

            var equalUnique = index.Current?.IsUnique == index.Defined?.IsUnique;
            if (equalUnique)
                sb.Append($"Uniqueness is correct. ");
            else
                sb.Append($"Current {index.Current?.IsUnique}, defined {index.Defined?.IsUnique}. ");

            var equalFields = (index.Current?.Fields.Order().ToArray() ?? []).SequenceEqual(index.Defined?.Fields.Order().ToArray() ?? []);
            if (equalFields)
                sb.Append("Fields are correct.");
            else
                sb.Append("Invalid fields.");

            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static IEnumerable<IndexModel> ToIndexModel(this CollectionInfo collectionInfo)
    {
        if (collectionInfo == null) yield break;

        var names = (collectionInfo.Index.Current?.Select(x => x.Name) ?? [])
            .Union(collectionInfo.Index.Defined?.Select(x => x.Name) ?? []);

        foreach (var x in names)
        {
            var current = collectionInfo.Index.Current?.SingleOrDefault(y => y.Name == x);
            var defined = collectionInfo.Index.Defined?.SingleOrDefault(y => y.Name == x);

            var equalFields = (current?.Fields.Order().ToArray() ?? []).SequenceEqual(defined?.Fields.Order().ToArray() ?? []);
            var equalUnique = current?.IsUnique == defined?.IsUnique;

            yield return new IndexModel
            {
                Name = x,
                Current = current,
                Defined = defined,
                EqualFields = equalFields && equalUnique
            };
        }
    }
}
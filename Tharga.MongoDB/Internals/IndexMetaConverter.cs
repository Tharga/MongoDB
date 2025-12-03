using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.MongoDB.Internals;

internal static class IndexMetaConverter
{
    public static IEnumerable<IndexMeta> BuildIndexMetas(this RepositoryCollectionBase instance)
    {
        var indicesProp = instance.GetType().GetProperty("Indices");
        var indices = indicesProp?.GetValue(instance) as IEnumerable;

        var coreIndicesProp = instance.GetType().GetProperty("CoreIndices", BindingFlags.NonPublic | BindingFlags.Instance);
        var coreIndices = coreIndicesProp?.GetValue(instance) as IEnumerable;

        var allIndices = indices.Union(coreIndices);

        var definedIndices = new List<IndexMeta>();
        if (allIndices is IEnumerable items)
        {
            foreach (var indexModel in items)
            {
                definedIndices.Add(IndexMetaConverter.ConvertToMetaDynamic(indexModel));
            }
        }
        var definedIndicesX = definedIndices.ToArray();
        return definedIndicesX;
    }

    // Called dynamically
    private static IndexMeta ConvertToMetaDynamic(object model)
    {
        var modelType = model.GetType();

        // Extract T from CreateIndexModel<T>
        var docType = modelType.GenericTypeArguments[0];

        var method = typeof(IndexMetaConverter)
            .GetMethod(nameof(ConvertToMeta), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(docType);

        return (IndexMeta)method.Invoke(null, new[] { model });
    }

    // Internal generic converter
    private static IndexMeta ConvertToMeta<T>(CreateIndexModel<T> model)
    {
        // You do NOT need IMongoDatabase – only a serializer + registry.
        var registry = BsonSerializer.SerializerRegistry;
        var serializer = registry.GetSerializer<T>();

        var args = new RenderArgs<T>(serializer, registry);

        // Now we can render the index key document
        var renderedKey = model.Keys.Render(args);
        var fields = renderedKey.Names.ToArray();

        return new IndexMeta
        {
            Name = model.Options.Name ?? string.Join("_", fields),
            Fields = fields,
            IsUnique = model.Options.Unique ?? false
        };
    }
}

public static class EnumerableExtensions
{
    public static IEnumerable Union(this IEnumerable first, IEnumerable second)
    {
        if (first != null)
        {
            foreach (var item in first)
            {
                yield return item;
            }
        }

        if (second != null)
        {
            foreach (var item in second)
            {
                yield return item;
            }
        }
    }
}

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
        var indices = ResolveProperty(instance, "Indices");
        var coreIndices = ResolveProperty(instance, "CoreIndices");

        // Order matches every other call site: CoreIndices first, then consumer Indices.
        var allIndices = coreIndices.Union(indices);

        var definedIndices = new List<IndexMeta>();
        foreach (var indexModel in allIndices)
        {
            definedIndices.Add(IndexMetaConverter.ConvertToMetaDynamic(indexModel));
        }
        return definedIndices.ToArray();
    }

    /// <summary>
    /// Walks the inheritance chain to find a property that may be declared as <c>internal</c> or
    /// <c>public</c> on a base class. Plain <c>GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)</c>
    /// does NOT find non-public members that are inherited from a base class, so we explicitly walk up.
    /// </summary>
    private static IEnumerable ResolveProperty(RepositoryCollectionBase instance, string propertyName)
    {
        var type = instance.GetType();
        while (type != null)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (prop != null)
            {
                return prop.GetValue(instance) as IEnumerable;
            }
            type = type.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Converts a single <see cref="CreateIndexModel{TEntity}"/> into an <see cref="IndexMeta"/>
    /// without involving reflection on the consumer type. Use this when you already have the
    /// model in hand (e.g. inside <c>UpdateIndicesBySchemaAsync</c>) and want lock-step pairing
    /// with the source array, instead of the reflection-driven <see cref="BuildIndexMetas"/>.
    /// </summary>
    internal static IndexMeta ConvertToMetaPublic<T>(CreateIndexModel<T> model) => ConvertToMeta(model);

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
        var fields = renderedKey.Elements.Select(e => $"{e.Name}_{e.Value}").ToArray();

        // Generate a MongoDB-compatible auto-name (e.g. "Name_1", "Country_1_Name_1")
        // so it aligns with the actual index name MongoDB assigns when no name is specified.
        var autoName = string.Join("_", renderedKey.Elements.Select(e => $"{e.Name}_{e.Value}"));

        return new IndexMeta
        {
            Name = model.Options.Name ?? autoName,
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

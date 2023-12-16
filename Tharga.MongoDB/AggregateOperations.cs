using MongoDB.Bson;
using System.Collections.Generic;
using System.Linq.Expressions;
using System;
using System.Linq;

namespace Tharga.MongoDB;

public class AggregateOperations<T>
{
    private readonly Dictionary<string, EAggregateOperation> _props = new();

    public AggregateOperations<T> Add<TProperty>(Expression<Func<T, TProperty>> propertyPicker, EAggregateOperation operation)
    {
        var me = (MemberExpression)propertyPicker.Body;
        _props.Add(me.Member.Name, operation);
        return this;
    }

    public Dictionary<string, BsonDocument> Build()
    {
        return _props.ToDictionary(x => x.Key, x => new BsonDocument($"${x.Value}".ToLower(), 1));
    }
}
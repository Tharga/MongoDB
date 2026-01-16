using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.MongoDB;

//TODO: Add clean helpers for sub-objects and sub-collections.
[BsonIgnoreExtraElements]
public abstract record PersistableEntityBase : ISupportInitialize
{
    [BsonExtraElements]
    protected Dictionary<string, object> CatchAll { get; init; }

    public virtual bool NeedsCleaning()
    {
        return CatchAll != null;
    }

    public virtual void BeginInit()
    {
    }

    public virtual void EndInit()
    {
        if (CatchAll == null) return;

        if (CatchAll.Any())
        {
            var message = $"There are {CatchAll.Count} unhandled fields for entity '{GetType().Name}'. One example is '{CatchAll.First().Key}'. If a field is removed or changed the action fo that field needs to be explicitly handled.";
            Debugger.Break();
            throw new InvalidOperationException(message);
        }
    }
}
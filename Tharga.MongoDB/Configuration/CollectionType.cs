using System;

namespace Tharga.MongoDB.Configuration;

public abstract class CollectionType
{
    public abstract Type Interface { get; }
    public abstract Type Implementation { get; }
}

public class CollectionType<TImplementation> : CollectionType
    where TImplementation : class
{
    public override Type Interface => typeof(TImplementation);
    public override Type Implementation => typeof(TImplementation);
}

public class CollectionType<TInterface, TImplementation>
    : CollectionType<TImplementation>
    where TImplementation : class
    where TInterface : IRepositoryCollection
{
    public override Type Interface => typeof(TInterface);
}
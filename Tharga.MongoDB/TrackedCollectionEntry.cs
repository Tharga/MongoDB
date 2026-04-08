using System;

namespace Tharga.MongoDB;

/// <summary>
/// Marker registered in DI by <see cref="MongoDbTrackingExtensions.TrackMongoCollection"/>
/// so that <c>UseMongoDB</c> can discover externally tracked collections.
/// </summary>
public sealed class TrackedCollectionEntry
{
    public TrackedCollectionEntry(Type serviceType, Type implementationType)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
    }

    public Type ServiceType { get; }
    public Type ImplementationType { get; }
}

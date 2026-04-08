using System;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.MongoDB;

/// <summary>
/// Extension methods for tracking external collections in the database monitor.
/// Use this when a package registers its own collection types via DI but needs
/// them to appear in the monitor as "in code" rather than "NotInCode".
/// </summary>
public static class MongoDbTrackingExtensions
{
    /// <summary>
    /// Tracks a collection type so the database monitor recognises it as registered in code.
    /// This does NOT register the type in DI — only tells the monitor about it.
    /// Can be called before or after <c>AddMongoDB</c>; the actual merge happens in <c>UseMongoDB</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceType">The collection interface type.</param>
    /// <param name="implementationType">The collection implementation type.</param>
    public static IServiceCollection TrackMongoCollection(this IServiceCollection services, Type serviceType, Type implementationType)
    {
        services.AddSingleton(new TrackedCollectionEntry(serviceType, implementationType));
        return services;
    }

    /// <summary>
    /// Tracks a collection type so the database monitor recognises it as registered in code.
    /// Generic convenience overload.
    /// </summary>
    /// <typeparam name="TService">The collection interface type.</typeparam>
    /// <typeparam name="TImplementation">The collection implementation type.</typeparam>
    public static IServiceCollection TrackMongoCollection<TService, TImplementation>(this IServiceCollection services)
    {
        return services.TrackMongoCollection(typeof(TService), typeof(TImplementation));
    }
}

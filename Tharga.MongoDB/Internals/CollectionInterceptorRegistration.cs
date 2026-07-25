using System;
using Tharga.MongoDB.Interception;

namespace Tharga.MongoDB.Internals;

/// <summary>
/// One entry in the ordered interceptor chain declared on <c>DatabaseOptions</c>. Exactly one of
/// <see cref="Type"/> and <see cref="Instance"/> is set — a type is resolved from DI when the
/// factory is built, an instance is used as supplied.
/// </summary>
internal sealed record CollectionInterceptorRegistration
{
    public Type Type { get; init; }
    public ICollectionInterceptor Instance { get; init; }
}

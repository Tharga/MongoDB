using Tharga.MongoDB.Tests.Lockable.Base;

namespace Tharga.MongoDB.Tests.Lockable.Renewable.Base;

/// <summary>
/// Shared base for renewable-lock tests. Reuses the Mongo/service-factory wiring and per-test
/// database teardown from <see cref="LockableTestBase"/>; renewable tests construct a
/// <see cref="RenewableLockableTestRepositoryCollection"/> (or the strict variant) against the
/// inherited factory.
/// </summary>
public abstract class RenewableLockableTestBase : LockableTestBase
{
}

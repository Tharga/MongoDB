# Bugfix: AddMongoDB throws on Lockable subclass with custom interface (#88)

## Originating

- **GitHub issue:** [#88](https://github.com/Tharga/MongoDB/issues/88)
- **Filed:** 2026-05-05 by Daniel Bohlin
- **Severity:** Bug, regression in 2.10.7
- **Affects:** Any consumer that subclasses `LockableRepositoryCollectionBase<TEntity, TKey>` AND declares a custom repository interface (the common pattern shown in the README).

## Reproduction

```csharp
internal interface IIntegrationRepositoryCollection
    : ILockableRepositoryCollection<Message, ObjectId>;

internal class IntegrationRepositoryCollection
    : LockableRepositoryCollectionBase<Message, ObjectId>, IIntegrationRepositoryCollection
{
    public IntegrationRepositoryCollection(...) : base(...) { }
}
```

`builder.AddMongoDB(...)` throws `InvalidOperationException`:

> There are 2 interfaces for collection type 'IntegrationRepositoryCollection' (IDocumentLeaseTransactionRunner, IIntegrationRepositoryCollection).

## Root cause

[Tharga.MongoDB/MongoDbRegistrationExtensions.cs:190-198](Tharga.MongoDB/MongoDbRegistrationExtensions.cs#L190-L198) auto-registers each non-generic, non-interface descendant of `IReadOnlyRepositoryCollection`. It picks the "service type" by looking at all interfaces the collection class implements, excluding two by hand (`IReadOnlyRepositoryCollection`, `IRepositoryCollection`), and throws if more than one remains.

When the multi-document transactions feature shipped (2.10.7), `LockableRepositoryCollectionBase` started implementing `IDocumentLeaseTransactionRunner` — another framework interface, not on the manual excludelist. Any lockable subclass that also declares its own repository interface now ends up with two surviving entries and trips the validation.

The validation itself is correct in spirit — *"a collection class should expose at most one consumer-facing interface so DI registration is unambiguous"* — but it conflates "consumer interfaces" with "all non-generic interfaces."

## Fix approach

Filter framework interfaces out of the count. Two options:

- **A. Namespace prefix exclude** — drop any interface whose namespace is `Tharga.MongoDB` or starts with `Tharga.MongoDB.`. Robust to future framework interfaces; one-line change.
- **B. Explicit excludelist** — add `IDocumentLeaseTransactionRunner` (and other known framework interfaces) to the existing `Where` clauses. Keeps the existing pattern but fragile — every future framework interface must remember to add itself here.

**Recommend A.** B trades correctness today for fragility tomorrow; the next framework interface added to the inheritance chain will silently re-introduce the same bug class.

## Scope

In:
- Fix the filter in `MongoDbRegistrationExtensions.cs`.
- TDD with red-green: failing test first, then the fix.
- Add complementary tests so the validation's *intended* behaviour stays locked down.

Out:
- Refactoring the auto-registration scan in general.
- Changing how `RegisterCollection` resolves implementation/service types.
- Bumping `MAJOR_MINOR` — this is a patch fix.

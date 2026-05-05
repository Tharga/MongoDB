# Plan — Fix #88 (TDD red-green)

## Step 1: Branch + baseline — DONE

- [x] `git checkout -b fix/issue-88-lockable-custom-interface master`
- [x] First commit: settings.json cleanup + plan/feature.md + plan/plan.md (carried in the same branch since they were uncommitted on master).

## Step 2: Locate test fixture pattern — DONE

- [x] Surveyed `Tharga.MongoDB.Tests/RegistrationOptionsTests.cs` and `TrackMongoCollectionTests.cs` for the pattern. New tests use `new ServiceCollection().AddLogging()` + an empty `ConfigurationBuilder` config.

## Step 3: Red — DONE

- [ ] In `Tharga.MongoDB.Tests/Registration/AddMongoDb_LockableSubclassWithCustomInterface_DoesNotThrow.cs` (or sibling location):
  ```csharp
  // private types in the same test file
  internal interface IIntegrationCollection : ILockableRepositoryCollection<Message, ObjectId> { }
  internal class IntegrationCollection : LockableRepositoryCollectionBase<Message, ObjectId>, IIntegrationCollection
  {
      public IntegrationCollection(IMongoDbServiceFactory factory) : base(factory) { }
      public override string CollectionName => "test-integration";
  }
  internal record Message : LockableEntityBase<ObjectId> { }
  ```
- [ ] The test:
  1. Builds an `IServiceCollection`.
  2. Calls `services.AddMongoDB(o => o.AddAutoRegistrationAssembly(typeof(IntegrationCollection).Assembly))`.
  3. Asserts no throw, and that the resulting `ServiceProvider` resolves `IIntegrationCollection`.
- [ ] Run only this test: should fail with the same `InvalidOperationException` from the issue. Confirm the exception message contains `IDocumentLeaseTransactionRunner` so we know the test is hitting the right path. **This is the red.**

## Step 4: Green — DONE

- [ ] Edit `Tharga.MongoDB/MongoDbRegistrationExtensions.cs:190-198`. Current shape:
  ```csharp
  var serviceTypes = collectionType.ImplementedInterfaces
      .Where(x => x.IsInterface && !x.IsGenericType)
      .Where(x => x != typeof(IReadOnlyRepositoryCollection))
      .Where(x => x != typeof(IRepositoryCollection))
      .ToArray();
  ```
- [ ] Replace the two specific excludes with a single namespace-prefix filter:
  ```csharp
  var serviceTypes = collectionType.ImplementedInterfaces
      .Where(x => x.IsInterface && !x.IsGenericType)
      .Where(x => !IsThargaMongoDBFrameworkInterface(x))
      .ToArray();
  ```
  …where `IsThargaMongoDBFrameworkInterface(Type)` is a small static helper:
  ```csharp
  private static bool IsThargaMongoDBFrameworkInterface(Type type)
      => type.Namespace != null
         && (type.Namespace == "Tharga.MongoDB" || type.Namespace.StartsWith("Tharga.MongoDB."));
  ```
- [ ] Re-run the test from Step 3. **Should pass — green.**

## Step 5: Lockdown — DONE

Add tests so future refactors can't quietly re-break either case:

- [ ] **Disk subclass with a custom interface** registers fine (covers the "working path" baseline so we'd notice if the fix also broke this).
- [ ] **Lockable subclass with no custom interface** registers fine (the implementation type is its own service type — already worked, but worth pinning).
- [ ] **Two genuine consumer interfaces still throws** — assert the validation fires for the case it was designed for. Build a class that implements two non-framework custom interfaces; expect `InvalidOperationException` and the same shape of message.
- [ ] All four tests run from the same fixture for consistency.

## Step 6: Regression sweep — DONE

- [ ] `dotnet test -c Debug` full suite. Expect the same baseline as master plus +4 (or +N) new passes. The 6 pre-existing replica-set transaction failures and the `GetLockedExpired` flake are expected; nothing else should turn red.
- [ ] `dotnet build -c Release` clean across net8/net9/net10.

## Step 7: Commit + PR

- [ ] Commit messages:
  - First commit: `test: add failing test for #88 (regression repro)` — includes only the new test class, asserts the buggy behaviour. Lets reviewer cleanly see the red.
  - Second commit: `fix: exclude Tharga.MongoDB framework interfaces from registration count (#88)` — the production fix only.
  - Third (optional): `test: add complementary registration tests for #88` — the lockdown tests from Step 5 if I split them out.
  - Squash on merge is fine if the project prefers a single commit; otherwise keep as is for the red-green narrative.
- [ ] Push, open PR `fix(#88): allow lockable subclass with custom interface`.
- [ ] PR body links to issue #88, summarises root cause, includes the test plan checklist.
- [ ] Wait for CI. On green, ask the user to merge.

## Open questions

- **Helper location.** `IsThargaMongoDBFrameworkInterface` is a one-call helper. Inline as a lambda vs. private static method on the extensions class. Default to private static — easier to write a unit test against if it grows.
- **Should `IDocumentLeaseTransactionRunner` be removed entirely?** It's an internal marker interface; if its only consumer is `WithTransactionAsync` reflection, maybe an attribute would be cleaner. Out of scope for this fix, but worth filing as a follow-up if true.

## Risks

- **The fix relies on namespace strings.** If we ever ship a framework interface in a non-`Tharga.MongoDB.*` namespace (e.g. a base interface in `Tharga.Toolkit.*`), the filter misses it and the bug returns. Mitigation: keep all framework interfaces under the `Tharga.MongoDB.*` namespace — already true; document as an invariant if needed.
- **Behaviour change for any user who legitimately has multiple custom interfaces** is unaffected — they still throw with the same message.

## Last session — implementation complete; awaits push + PR

All 6 implementation steps done. Committed sequence on `fix/issue-88-lockable-custom-interface`:

1. `dd8f469` — settings.json cleanup + plan baseline
2. `a23a6a5` — red test (regression repro)
3. `51b2db8` — fix: assembly-based framework-interface check + green test refinement (asserts on `IServiceCollection` registration entry, not full DI resolution)
4. `72ae593` — 5 lockdown unit tests for `ResolveCollectionServiceType` helper

**Important deviation from the original plan:** the helper started as a namespace-prefix check (`StartsWith("Tharga.MongoDB.")`), but that erroneously matches the `Tharga.MongoDB.Tests` namespace and filtered out *test* interfaces too. Switched to an assembly check (`type.Assembly == typeof(IRepositoryCollection).Assembly`) which is both more correct and more robust to future namespace reorganisations.

**Refactor done as part of green:** extracted the resolution loop body to an internal helper `ResolveCollectionServiceType(TypeInfo)` so the validation can be unit-tested without going through the full `AddMongoDB` machinery. Helper is `internal` and visible to tests via the existing `InternalsVisibleTo("Tharga.MongoDB.Tests")`.

Tests: 6 new (1 integration + 5 lockdown). Full regression: 395 passed / 8 skipped / 5 pre-existing failures (replica-set transaction tests). Release build clean across net8/9/10.

Next: Step 7 — push + PR + closure (archive plan/feature.md to external `done/`).

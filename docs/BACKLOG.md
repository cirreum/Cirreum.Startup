# Backlog

Deferred work for **Cirreum.Startup**. Items here are tracked but not yet
ready to ship — either because the cost outweighs the benefit in isolation,
or because they're waiting on a forcing function (a related change, a
consumer upgrade, a coordinated multi-repo rollout).

## How this file works

- Each item is a `###` heading so it can be linked to and parsed.
- Each item declares **`SemVer:`** (`Patch` | `Minor` | `Major` | `Unspecified`),
  **`Trigger:`** (the human-readable condition that will make it ready), and
  **`Noted:`** (the date the item was added).
- The Cirreum DevOps release scripts (`PatchRelease`, `MinorRelease`,
  `MajorRelease`) surface items at-or-below the requested bump level so the
  operator can decide whether to fold them in before tagging.
- Items that ship: move from this file to `docs/CHANGELOG.md` under
  `[Unreleased]`. Items that grow into design discussions: promote to an ADR.

## Queued

### Initializer scan: re-filter the `ReflectionTypeLoadException` fallback and exclude abstract types

**SemVer:** Patch
**Trigger:** Next Cirreum.Startup release.
**Noted:** 2026-07-05

Two hardening gaps in the initializer scan (`Extensions/ServiceCollectionExtensions.cs`),
found during the ADR-0025 adversarial review of `Cirreum.Runtime.Authentication`'s
`ISystemInitializer` bootstrap:

1. The `ReflectionTypeLoadException` fallback returns the assembly's loadable types
   **without re-applying the `IsImplemented` predicate**, so a partially-loadable
   assembly gets arbitrary types registered as initializer descriptors — the provider
   build then fails with a confusing "type can't be converted to service type" error
   far from the cause. Apply the predicate to `ex.Types` (nulls filtered) in the
   fallback.
2. `IsImplemented` doesn't exclude abstract classes, so an abstract base implementing
   `ISystemInitializer`/`IAutoInitialize`/`IStartupTask` would be registered and fail
   at activation. Add `!type.IsAbstract`.

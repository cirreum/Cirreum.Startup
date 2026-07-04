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

### Harden the `IAutoInitialize` discovery scan

- **SemVer:** Patch
- **Trigger:** Next Cirreum.Startup release (no specific blocker)
- **Noted:** 2026-07-04

**Why:** `ServiceCollectionExtensions.AddServicesFor<IAutoInitialize>` has
three fragilities discovered while wiring the Runtime.Messaging registry
bootstrap (2026-07-04):

1. **The primary-interface naming rule strips ALL capital I's, not just the
   prefix** — `i.Name.Replace("I", "")`. An interface with a mid-name capital
   I (e.g. `IFooInitializer` → `"Foonitializer"`) can never pass the
   `implName.Contains(...)` check, so a conventionally-named
   `FooInitializer : IFooInitializer` **throws at startup** ("Cannot register
   … without a properly named primary interface"). Fix: strip only the
   leading `I` (`i.Name[1..]` guarded by the `I{Upper}` convention).
2. **Factory registrations are invisible to the pre-registered path.**
   The existing-descriptor match only compares `ImplementationType`, which is
   `null` for `AddSingleton<IFoo>(sp => …)` registrations. The scan then falls
   through to the auto-register branch: the `TryAdd` no-ops (the service type
   is taken) and tracking usually still resolves the right instance, but if
   the naming rule fails on that fallback the host throws even though the app
   registered the service correctly. Fix: treat "an existing descriptor whose
   ServiceType is assignable from the impl type" (or whose factory/instance
   produces it) as pre-registered, or at minimum skip the throw when the
   service is already resolvable.
3. **Selection nondeterminism** — the primary interface is chosen by
   `FirstOrDefault` over `GetImmediateInterfaces()` (unordered set semantics)
   with the name-contains rule; e.g. `IDisposable` → `"Disposable"` can
   collide with a class name like `DisposableCache`. Fix: prefer an interface
   that itself derives from `IAutoInitialize` when one exists, then apply the
   naming rule; consider a clearer exception message listing the candidates.

Also worth a look while in there: `AutoInitializeServices` is a `static
List<Type>` on the extension class — shared across hosts in the same process
(test hosts, WebApplicationFactory), not thread-safe, and cleared by whichever
host initializes first.

Behavioral guardrail: all current in-tree consumers (impl-class pattern like
`SessionManager : ISessionManager, IAutoInitialize`, and interface-extends
like `IThemeMonitor : IAutoInitialize`) must keep resolving to the same
service types. `ISystemInitializer` / `IStartupTask` discovery
(`TryAddEnumerable` against the marker interface) has none of these issues
and needs no change — where a component's only interface would be the marker
itself, `ISystemInitializer` is the better home (see
`DistributedMessageRegistryBootstrap` in Cirreum.Runtime.Messaging).

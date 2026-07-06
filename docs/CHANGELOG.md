# Cirreum.Startup Changelog

All notable changes to **Cirreum.Startup** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

### Fixed

- **Auto-initialize tracking is now container-scoped (ADR-0028).** The process-global static tracking list is gone — `AddApplicationInitializers()` records tracked service types in an internal manifest registered into the same service collection, so multiple hosts in one process are fully isolated: no cross-container resolution failures, no global wipes, no double initialization when the registration scan runs twice. `ClearAutoInitializeServices()` keeps its signature but now clears *this container's* tracking only — the deliberate per-container opt-out it always advertised.
- **Resolution is loud (ADR-0028).** Auto-registration remains try-first (a manual application registration wins and is tracked as-is — including a *different* conforming implementation the app pre-registered for the same primary interface), but by resolution time every tracked type must produce an `IAutoInitialize`: a registration removed or replaced with a non-conforming implementation after the scan now fails `GetAutoInitializeServices()` with an actionable error instead of being silently skipped. The result is materialized at the call (no deferred iterator) and deduplicated by instance.
- **Primary-interface collisions between two discovered implementations fail fast** at registration with both type names — previously the second implementation was silently never registered (and never initialized) while the first initialized twice. Pre-existing application registrations are exempt: they are deliberate composition and always win.
- **Manual registrations the scan cannot see now initialize (ADR-0028).** `IAutoInitialize`'s second purpose — initialize all/any registered services that implement the marker, regardless of how they were registered — was only scan-mediated: a manual registration whose implementation the scan couldn't discover (a closed generic, an instance, a factory) was silently never initialized. A marked-registration sweep now tracks every registration that visibly carries the marker: on its service type, its implementation type, or its registered instance. A factory whose service type doesn't carry the marker remains unvettable at composition time — carry the marker on the service interface for factory shapes.
- **Auto-initialized services are single-slot (ADR-0028).** A tracked service identity must have exactly one registration — the one that resolves. More than one registration of a tracked identity (two implementations under a marker-carrying interface, or a marked registration shadowed by a later one) now fails loudly at composition time — previously the last registration silently won and any shadowed marked service was never initialized. Multiple marked implementations each get their own service identity; set-style services consumed as `IEnumerable<T>` are initialized by their owning host, not by auto-initialization. Marker placement never affects cardinality — implementation-carried (recommended; keeps `InitializeAsync` off the consumer contract) vs interface-carried (makes factory registrations vettable) is a DX choice only.
- The no-conventional-pairing failure is now an `InvalidOperationException` (was a bare `Exception`) and names the remedy: register the service manually before `AddApplicationInitializers()` — the try-first scan honors an existing registration and still auto-initializes it.
- **Displacement is conformance-gated (ADR-0028).** When the application pre-registers a *different* implementation on a discovered implementation's primary interface, the displacement is quiet only when the occupier provably auto-initializes too (the interface carries the marker — structural — or the occupier's implementation type/instance does): the slot still initializes, with the app's chosen implementation. A non-conforming occupier now fails loudly at scan — previously the discovered implementation was silently dropped and nothing initialized for the slot, surfacing only later (or never). The try-first instance check was also tightened: a registered instance must actually *be* the discovered implementation (an instance of a different type routes to the displacement gate instead of masquerading as "already registered").
- **Scan hardening:** the `ReflectionTypeLoadException` fallback re-applies the implements-filter to the loadable types (previously it returned them unfiltered, registering arbitrary types as initializers); abstract classes and open generic type definitions are excluded from discovery (either previously broke registration or the provider build).
- First test suite for the repo (29 tests): multi-container isolation, per-container Clear, tracking dedup, the loud-resolution contract, try-first precedence including pre-registered alternates and factories, same-scan collision fail-fast, single-slot cardinality violations (interface-carried ×2, shadowed marked registration) and the sanctioned own-identity shape, the marked-registration sweep shapes, the scan exclusions, and end-to-end runs through the real `ApplicationInitializer` for the original use case — an interface/impl pair with the marker on the implementation and `InitializeAsync` *explicitly* implemented (kept off the consumer-facing surface), discovered, registered, invoked exactly once, and not re-invoked on a second initialization pass.

## [1.0.120] - 2026-07-05

### Fixed

- CI's publish workflow still ran restore/build/pack from a `working-directory: ./src` override left over from before `Cirreum.Startup.slnx` moved to the repo root — broke the `1.0.119` publish (`MSB1009`, project file not found). No effect on the package itself; this release exists to re-trigger a working publish run.

## [1.0.119] - 2026-07-05

### Fixed

- `AddServicesFor<IAutoInitialize>`'s primary-interface naming rule stripped every capital `I` in an interface name, not just the conventional leading one — a mid-name capital `I` (e.g. `IFooInitializer`) could never match its own class name, causing a conventionally-named `FooInitializer : IFooInitializer` to throw at startup. Now strips only the leading `I`, guarded by the `I{Upper}` convention.
- Factory- and instance-based registrations (e.g. `AddSingleton<IFoo>(sp => ...)`) were invisible to the pre-registered-service check, which only matched on `ImplementationType` — a correctly self-registered service could still fall through to the naming-rule scan and throw. The check now also recognizes an existing descriptor registered via factory or instance whose `ServiceType` the discovered type satisfies.
- Primary-interface selection was nondeterministic (`FirstOrDefault` over unordered `GetImmediateInterfaces()`), risking an unrelated interface (e.g. `IDisposable`) winning by name collision over the intended one. Selection now prefers a candidate that itself derives from the initializer marker (e.g. `IThemeMonitor : IAutoInitialize`) before falling back to a plain name match, and the failure message now lists every candidate interface considered.
- `AutoInitializeServices` (the static tracking list) was not thread-safe — concurrent host initialization in the same process (e.g. parallel test hosts) could corrupt it. Mutations and reads are now synchronized, and enumeration works over a snapshot.
- `ApplicationInitializer`'s `ActivitySource` was an instance field instead of `static readonly`, inconsistent with every other telemetry class in the framework and a latent trap if its DI lifetime is ever changed from singleton.

All current in-tree consumer patterns (impl-class, e.g. `SessionManager : ISessionManager, IAutoInitialize`, and interface-extends, e.g. `IThemeMonitor : IAutoInitialize`) continue to resolve to the same service types as before.

# Cirreum.Startup Changelog

All notable changes to **Cirreum.Startup** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

## [1.0.119] - 2026-07-05

### Fixed

- `AddServicesFor<IAutoInitialize>`'s primary-interface naming rule stripped every capital `I` in an interface name, not just the conventional leading one — a mid-name capital `I` (e.g. `IFooInitializer`) could never match its own class name, causing a conventionally-named `FooInitializer : IFooInitializer` to throw at startup. Now strips only the leading `I`, guarded by the `I{Upper}` convention.
- Factory- and instance-based registrations (e.g. `AddSingleton<IFoo>(sp => ...)`) were invisible to the pre-registered-service check, which only matched on `ImplementationType` — a correctly self-registered service could still fall through to the naming-rule scan and throw. The check now also recognizes an existing descriptor registered via factory or instance whose `ServiceType` the discovered type satisfies.
- Primary-interface selection was nondeterministic (`FirstOrDefault` over unordered `GetImmediateInterfaces()`), risking an unrelated interface (e.g. `IDisposable`) winning by name collision over the intended one. Selection now prefers a candidate that itself derives from the initializer marker (e.g. `IThemeMonitor : IAutoInitialize`) before falling back to a plain name match, and the failure message now lists every candidate interface considered.
- `AutoInitializeServices` (the static tracking list) was not thread-safe — concurrent host initialization in the same process (e.g. parallel test hosts) could corrupt it. Mutations and reads are now synchronized, and enumeration works over a snapshot.
- `ApplicationInitializer`'s `ActivitySource` was an instance field instead of `static readonly`, inconsistent with every other telemetry class in the framework and a latent trap if its DI lifetime is ever changed from singleton.

All current in-tree consumer patterns (impl-class, e.g. `SessionManager : ISessionManager, IAutoInitialize`, and interface-extends, e.g. `IThemeMonitor : IAutoInitialize`) continue to resolve to the same service types as before.

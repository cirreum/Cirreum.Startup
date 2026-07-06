# Cirreum - Startup Library

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.Startup.svg?style=flat-square)](https://www.nuget.org/packages/Cirreum.Startup/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.Startup.svg?style=flat-square)](https://www.nuget.org/packages/Cirreum.Startup/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.Startup?style=flat-square)](https://github.com/cirreum/Cirreum.Startup/releases)

This package defines contracts and extension methods for startup services in C# .NET and Blazor applications.

## Description

Cirreum.Startup allows developers to define one or more classes that implement specific interfaces, which are then automatically executed during application startup. This is particularly useful for initializing services, performing setup tasks, or executing configuration logic before the application begins its normal operation.

## Key Interfaces

### 1. ISystemInitializer

- Purpose: Defines a service that performs core application initialization, setup, or configuration.
- Execution Order: Runs first, before any other startup services.
- Usage: Typically used by component/library/service authors for critical system setup.
- Restrictions: Must not depend on other startup interfaces.

### 2. IAutoInitialize

- Purpose: Allows existing services to be automatically initialized during application startup.
- Execution Order: Runs after ISystemInitializer and before IStartupTask.
- Usage: Useful for services that need initialization before they can be used.
- Auto-registration: The system attempts to auto-register services implementing this interface — a registration your application already made always wins and is used as-is. Place the marker on the implementation (recommended — it keeps `InitializeAsync` off the consumer-facing contract) or on its paired service interface. Non-conventional naming (the implementation name doesn't contain the interface name) requires manual registration; the scan fails loudly otherwise.
- Replacement: Registering your own implementation for a discovered service's interface displaces the discovered one quietly only when your registration provably auto-initializes too (the slot still initializes, with your implementation). An occupier that cannot initialize fails loudly at startup composition — a marked implementation is never silently dropped in a way that leaves its service uninitialized.
- Initialization: Regardless of how a service was registered — auto-registered or registered manually — any registration that visibly carries `IAutoInitialize` (on its service type, implementation type, or registered instance) is initialized at startup. A factory registration is only visible when the marker is on the service interface.
- Single-slot: An auto-initialized service identity has exactly one registration — the one that resolves. Multiple marked implementations each get their own service identity and are resolved individually; more than one registration of a tracked identity fails loudly at composition time. Set-style services consumed as `IEnumerable<T>` are initialized by their owning host, not by auto-initialization.
- Fail-fast: A tracked service that no longer resolves to an `IAutoInitialize` at startup (its registration was removed or replaced) fails the host with an actionable error rather than being silently skipped. To deliberately opt a container out of auto-initialization, call `ClearAutoInitializeServices()` on its service provider.

### 3. IStartupTask

- Purpose: Defines a service that performs tasks after the application has been initialized.
- Execution Order: Runs last, after ISystemInitializer and IAutoInitialize.
- Customizable Priority: Includes an `Order` property to specify execution priority.

## Important Notes

- These interfaces are intended for Singleton services (Scoped in Blazor WASM).
- Services implementing these interfaces are materialized during startup and are not disposed.
- Each interface's primary method (RunAsync, InitializeAsync, ExecuteAsync) is called only once during application startup.

## Usage

Implement the appropriate interface based on your initialization needs:

- Use `ISystemInitializer` for core system setup.
- Use `IAutoInitialize` for general service initialization.
- Use `IStartupTask` for tasks that should run after the application is fully initialized.

Refer to individual interface documentation for detailed usage examples and restrictions.

---

**Cirreum Foundation Framework** - Layered simplicity for modern .NET
namespace Cirreum.Startup;

/// <summary>
/// Defines a contract for services that require automatic initialization during application startup.
/// </summary>
/// <remarks>
/// <para>
/// Classes implementing this interface are automatically discovered and registered
/// during startup. In ASP.NET server-side applications, they are registered as
/// singletons. In Blazor WebAssembly standalone applications, they are registered
/// with scoped lifetime due to WebAssembly's single-scope architecture.
/// </para>
/// <para>
/// The system automatically registers implementations using convention-based naming:
/// for a service named IMyService, the implementation should be named MyService.
/// The system will use any pre-existing registration if found. For non-conventional
/// naming, services must be registered manually before the auto-registration phase.
/// </para>
/// <para>
/// This interface is ideal for services that require one-time asynchronous initialization
/// before they can be used. Initialization occurs after all <see cref="ISystemInitializer"/>
/// services have completed and before any <see cref="IStartupTask"/> services begin.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MyService : IMyService, IAutoInitialize 
/// {
///     private readonly SomeDependency _dependency;
///     
///     public MyService(SomeDependency dependency)
///     {
///         _dependency = dependency;
///     }
///     
///     public async ValueTask InitializeAsync()
///     {
///         await _dependency.PrepareAsync();
///         // Other initialization logic...
///     }
/// }
/// </code>
/// </example>
public interface IAutoInitialize {
	/// <summary>
	/// Executes one-time initialization logic for this service.
	/// </summary>
	/// <returns>An awaitable <see cref="ValueTask"/>.</returns>
	ValueTask InitializeAsync();
}
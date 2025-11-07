namespace Cirreum.Startup;

/// <summary>
/// A special interface for defining a service that will perform core system-level
/// initialization during application startup.
/// </summary>
/// <remarks>
/// <para>
/// Classes implementing this interface are automatically discovered and registered
/// during startup. In ASP.NET server-side applications, they are registered as
/// singletons. In Blazor WebAssembly standalone applications, they are registered
/// with scoped lifetime due to WebAssembly's single-scope architecture.
/// </para>
/// <para>
/// An implementation of this interface should be purpose specific (SRP) and focused
/// on essential system-level initialization. This is typically used by core framework
/// and infrastructure components. For application-level initialization, see
/// <see cref="IStartupTask"/> instead.
/// </para>
/// <para>
/// A service that implements this interface MUST NOT implement or take a dependency
/// on any other <see cref="ISystemInitializer"/>, 
/// <see cref="IAutoInitialize"/> or 
/// <see cref="IStartupTask"/> services.
/// System initializers are executed first in the startup sequence, followed by
/// auto-initializers and startup tasks.
/// </para>
/// <para>
/// Note: These services are instantiated and executed during the app.RunAsync phase,
/// after service registration (builder.AddXXX) and middleware configuration (app.UseXXX,
/// app.Map), but before the main application runtime starts.
/// </para>
/// </remarks>
public interface ISystemInitializer {
	/// <summary>
	/// Executes the system initialization logic.
	/// </summary>
	/// <param name="serviceProvider">The root service provider.</param>
	/// <returns>An awaitable <see cref="ValueTask"/>.</returns>
	ValueTask RunAsync(IServiceProvider serviceProvider);
}
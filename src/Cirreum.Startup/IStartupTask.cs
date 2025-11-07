namespace Cirreum.Startup;

/// <summary>
/// Defines a contract for services that need to execute ordered startup tasks
/// before the application begins running.
/// </summary>
/// <remarks>
/// <para>
/// Classes implementing this interface are automatically discovered and registered
/// during startup. In ASP.NET server-side applications, they are registered as
/// singletons. In Blazor WebAssembly standalone applications, they are registered
/// with scoped lifetime due to WebAssembly's single-scope architecture.
/// </para>
/// <para>
/// Startup tasks are executed in order based on their Order property (lower values
/// execute first). All startup tasks run after both <see cref="ISystemInitializer"/>
/// and <see cref="IAutoInitialize"/> services have completed, but before the
/// application's main execution begins.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class DatabaseSetupTask : IStartupTask 
/// {
///     private readonly DbContext _context;
///     
///     public DatabaseSetupTask(DbContext context)
///     {
///         _context = context;
///     }
///     
///     public int Order => 100; // Runs after tasks with lower values
///     
///     public async ValueTask ExecuteAsync()
///     {
///         await _context.Database.MigrateAsync();
///         // Other startup logic...
///     }
/// }
/// </code>
/// </example>
public interface IStartupTask {
	/// <summary>
	/// Determines the execution order of this startup task. Tasks with lower
	/// values execute before tasks with higher values.
	/// </summary>
	int Order { get; }

	/// <summary>
	/// Executes the startup task logic.
	/// </summary>
	/// <returns>An awaitable <see cref="ValueTask"/>.</returns>
	ValueTask ExecuteAsync();
}
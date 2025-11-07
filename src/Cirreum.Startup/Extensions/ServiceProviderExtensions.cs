namespace Microsoft.Extensions.DependencyInjection;

using Cirreum.Startup;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

/// <summary>
/// Extension methods for IServiceProvider related to application initialization.
/// </summary>
public static class ServiceProviderExtensions {

	private static ApplicationInitializer? _initializer;

	/// <summary>
	/// Initializes the application using the Cirreum.Startup framework.
	/// </summary>
	/// <param name="serviceProvider">The service provider.</param>
	/// <returns>A ValueTask representing the asynchronous operation.</returns>
	public static async ValueTask InitializeApplicationAsync(this IServiceProvider serviceProvider) {

		if (_initializer is not null) {
			throw new InvalidOperationException("Application Startup has already been initialized.");
		}

		// Force the TracerProvider (and its ActivityListener) to be created now
		_ = serviceProvider.GetService<TracerProvider>();

		var logger = serviceProvider
			.GetRequiredService<ILoggerFactory>()
			.CreateLogger<ApplicationInitializer>();

		_initializer = new ApplicationInitializer(logger, serviceProvider);

		await _initializer.InitializeApplication();

	}

}
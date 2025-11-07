namespace Cirreum.Startup;

using Microsoft.Extensions.Logging;

internal static partial class LoggingExtensions {

	//
	// InitializeApplication
	//
	[LoggerMessage(
		Level = LogLevel.Debug,
		Message = "Starting application initialization sequence")]
	internal static partial void LogInitializationStarting(this ILogger logger);

	[LoggerMessage(
		Level = LogLevel.Debug,
		Message = "Application initialization completed successfully in {durationMs}ms")]
	internal static partial void LogInitializationCompleted(this ILogger logger, double durationMs);

	[LoggerMessage(
		Level = LogLevel.Error,
		Message = "Application initialization failed")]
	internal static partial void LogInitializationFailed(this ILogger logger, Exception ex);


	//
	// System Initializer
	//
	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Running all system initializers")]
	internal static partial void LogRunningSystemInitializers(this ILogger logger);

	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Running System Initializer: {initializerName}")]
	internal static partial void LogRunSystemInitializer(this ILogger logger, string initializerName);

	[LoggerMessage(
		Level = LogLevel.Error,
		Message = "Error running System Initializer: {InitializerName}")]
	internal static partial void LogRunSystemInitializerFailed(this ILogger logger, Exception ex, string initializerName);

	//
	// Auto-Initialize Services
	//
	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Initializing all auto-initializing services")]
	internal static partial void LogInitializingAutoInitializeServices(this ILogger logger);

	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Initializing service : {ServiceName}")]
	internal static partial void LogAutoInitializingService(this ILogger logger, string serviceName);

	[LoggerMessage(
		Level = LogLevel.Error,
		Message = "Error initializing service: {ServiceName}")]
	internal static partial void LogAutoInitializingServiceFailed(this ILogger logger, Exception ex, string serviceName);

	//
	// Startup Tasks
	//
	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Executing all startup tasks")]
	internal static partial void LogExecutingStartupTasks(this ILogger logger);

	[LoggerMessage(
		Level = LogLevel.Trace,
		Message = "Executing Startup Task: {StartupTask} (Order: {Order})")]
	internal static partial void LogExecutingStartupTask(this ILogger logger, string startupTask, int order);

	[LoggerMessage(
		Level = LogLevel.Error,
		Message = "Error executing Startup Task: {StartupTask} (Order: {Order})")]
	internal static partial void LogExecutingStartupTaskFailed(this ILogger logger, Exception ex, string startupTask, int order);

}
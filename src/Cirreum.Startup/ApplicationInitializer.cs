namespace Cirreum.Startup;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Provides initialization methods for applications using the Cirreum.Startup framework.
/// </summary>
internal class ApplicationInitializer(
	ILogger<ApplicationInitializer> logger,
	IServiceProvider serviceProvider) {

	internal const string LibName = "Cirreum.Startup";

	private const string InitializersDurationTag = "initializers.duration_ms";

	private const string SystemInitializersEvent = "SystemInitializers";
	private const string AutoInitializersEvent = "AutoInitializeServices";
	private const string StartupTasksEvent = "StartupTasks";

	private const string InitializerTypeName = "initializer.type";
	private const string InitializerOrderName = "initializer.order";
	private const string InitializerCategoryName = "initializer.category";

	private const string InitializerCategorySystem = "system";
	private const string InitializerCategoryAuto = "auto";
	private const string InitializerCategoryStartup = "startup";

	private const string ActivityName = nameof(InitializeApplication);
	private readonly ActivitySource _activitySource = new(LibName);

	/// <summary>
	/// Runs the complete initialization sequence: System initializers, Auto initializers, and Startup tasks.
	/// </summary>
	/// <returns>A ValueTask representing the asynchronous operation.</returns>
	public async ValueTask InitializeApplication() {

		using var initActivity = this._activitySource.StartActivity(ActivityName);

		using var scope = logger.BeginScope(ActivityName);
		logger.LogInitializationStarting();


		// Run each phase in the proper order (System Initializers, Auto-Init Services, and then Startup Tasks)
		var watch = Stopwatch.StartNew();
		try {

			await this.RunSystemInitializers();

			await this.RunAutoInitializers();

			await this.RunStartupTasks();

			watch.Stop();
			var duration = watch.ElapsedMilliseconds;
			initActivity?.AddTag(InitializersDurationTag, duration.ToString("F2"));
			logger.LogInitializationCompleted(duration);

		} catch (Exception ex) {
			watch.Stop();
			var duration = watch.ElapsedMilliseconds;
			initActivity?.AddTag(InitializersDurationTag, duration.ToString("F2"));
			initActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			logger.LogInitializationFailed(ex);
			throw;
		}

	}

	/// <summary>
	/// Runs all registered system initializers.
	/// </summary>
	/// <returns>A ValueTask representing the asynchronous operation.</returns>
	private async ValueTask RunSystemInitializers() {
		Activity.Current?.AddEvent(new ActivityEvent(SystemInitializersEvent + ".Started"));
		try {
			logger.LogRunningSystemInitializers();
			var svcs = serviceProvider.GetServices<ISystemInitializer>();
			foreach (var svc in svcs) {
				var initializerType = svc.GetType();
				logger.LogRunSystemInitializer(initializerType.Name);
				using var initActivity = this._activitySource.StartActivity(
					initializerType.Name,
					ActivityKind.Internal,
					Activity.Current?.Context ?? new ActivityContext(),
					tags: [
						new(InitializerTypeName, initializerType.FullName),
					new(InitializerCategoryName, InitializerCategorySystem)
					]);
				try {
					await svc.RunAsync(serviceProvider);
				} catch (Exception ex) {
					logger.LogRunSystemInitializerFailed(ex, initializerType.Name);
					initActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
					throw;
				}
			}
		} finally {
			Activity.Current?.AddEvent(new ActivityEvent(SystemInitializersEvent + ".Completed"));
		}
	}

	/// <summary>
	/// Runs all registered auto initializers.
	/// </summary>
	/// <returns>A ValueTask representing the asynchronous operation.</returns>
	private async ValueTask RunAutoInitializers() {
		Activity.Current?.AddEvent(new ActivityEvent(AutoInitializersEvent + ".Started"));
		try {
			logger.LogInitializingAutoInitializeServices();
			var svcs = serviceProvider.GetAutoInitializeServices();
			foreach (var svc in svcs) {
				var initializerType = svc.GetType();
				logger.LogAutoInitializingService(initializerType.Name);
				using var initActivity = this._activitySource.StartActivity(
					initializerType.Name,
					ActivityKind.Internal,
					Activity.Current?.Context ?? new ActivityContext(),
					tags: [
						new(InitializerTypeName, initializerType.FullName),
						new(InitializerCategoryName, InitializerCategoryAuto)
					]);
				try {
					await svc.InitializeAsync();
				} catch (Exception ex) {
					logger.LogAutoInitializingServiceFailed(ex, initializerType.Name);
					initActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
					throw;
				}
			}
			serviceProvider.ClearAutoInitializeServices();
		} finally {
			Activity.Current?.AddEvent(new ActivityEvent(AutoInitializersEvent + ".Completed"));
		}
	}

	/// <summary>
	/// Runs all registered startup tasks in order.
	/// </summary>
	/// <returns>A ValueTask representing the asynchronous operation.</returns>
	private async ValueTask RunStartupTasks() {
		Activity.Current?.AddEvent(new ActivityEvent(StartupTasksEvent + ".Started"));
		try {
			logger.LogExecutingStartupTasks();
			var svcs = serviceProvider.GetServices<IStartupTask>();
			foreach (var svc in svcs.OrderBy(s => s.Order)) {
				var initializerType = svc.GetType();
				logger.LogExecutingStartupTask(initializerType.Name, svc.Order);
				using var initActivity = this._activitySource.StartActivity(
					initializerType.Name,
					ActivityKind.Internal,
					Activity.Current?.Context ?? new ActivityContext(),
					tags: [
						new(InitializerTypeName, initializerType.FullName),
					new(InitializerCategoryName, InitializerCategoryStartup),
					new(InitializerOrderName, svc.Order.ToString())
					]);
				try {
					await svc.ExecuteAsync();
				} catch (Exception ex) {
					logger.LogExecutingStartupTaskFailed(ex, initializerType.Name, svc.Order);
					initActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
					throw;
				}
			}
		} finally {
			Activity.Current?.AddEvent(new ActivityEvent(StartupTasksEvent + ".Completed"));
		}
	}

}
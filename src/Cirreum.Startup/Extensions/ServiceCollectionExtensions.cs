namespace Microsoft.Extensions.DependencyInjection;

using Cirreum.Startup;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Linq;
using System.Reflection;


/// <summary>
/// Startup extension methods.
/// </summary>
public static class ServiceCollectionExtensions {

	private static readonly Lock AutoInitializeLock = new();
	internal static List<Type> AutoInitializeServices = [];

	/// <summary>
	/// Register all types that implement <see cref="ISystemInitializer"/>,
	/// <see cref="IAutoInitialize"/> or <see cref="IStartupTask"/> with
	/// the <see cref="IServiceCollection"/>.
	/// </summary>
	/// <param name="services">The <see cref="IServiceCollection"/> to add the initializers to.</param>
	/// <param name="serviceLifetime">The desired lifetime of initialize/start up services. Default: Singleton</param>
	/// <returns>The source <see cref="IServiceCollection"/>.</returns>
	/// <remarks>
	/// <para>
	/// Initializers should be added to services during the builder phase.
	/// </para>
	/// </remarks>
	public static IServiceCollection AddApplicationInitializers(
		this IServiceCollection services,
		ServiceLifetime serviceLifetime = ServiceLifetime.Singleton) {

		if (!OperatingSystem.IsBrowser()) {
			services.AddOpenTelemetry()
				.WithMetrics(metrics => metrics
					.AddMeter(ApplicationInitializer.LibName))
				.WithTracing(tracing => tracing
					.AddSource(ApplicationInitializer.LibName));
		}

		services.AddServicesFor<ISystemInitializer>(serviceLifetime);
		services.AddServicesFor<IAutoInitialize>(serviceLifetime, true);
		services.AddServicesFor<IStartupTask>(serviceLifetime);

		return services;
	}

	/// <summary>
	/// Resolves all the registered <see cref="IAutoInitialize"/> services.
	/// </summary>
	/// <param name="sp">The <see cref="IServiceProvider"/> that will provide the service implementations.</param>
	/// <returns>The enumerable collection of services.</returns>
	public static IEnumerable<IAutoInitialize> GetAutoInitializeServices(this IServiceProvider sp) {
		Type[] snapshot;
		lock (AutoInitializeLock) {
			snapshot = [.. AutoInitializeServices];
		}
		foreach (var svc in snapshot) {
			var castedSvc = sp.GetRequiredService(svc) as IAutoInitialize;
			if (castedSvc is not null) {
				yield return castedSvc;
			}
		}
	}

	/// <summary>
	/// Removes any <see cref="IAutoInitialize"/> service implementation
	/// being tracked for initialization.
	/// </summary>
	public static void ClearAutoInitializeServices(this IServiceProvider _) {
		lock (AutoInitializeLock) {
			AutoInitializeServices.Clear();
		}
	}


	private static IServiceCollection AddServicesFor<TServiceType>(
		this IServiceCollection services,
		ServiceLifetime serviceLifetime,
		bool isAutoInitService = false) {

		var serviceType = typeof(TServiceType);
		foreach (var implementationType in DiscoverServiceImplementations(serviceType)) {

			if (isAutoInitService) {

				var foundSvcDescriptor = services.FirstOrDefault(d =>
					d.IsKeyedService is false &&
					(
						(d.ImplementationType is not null && d.ImplementationType.Equals(implementationType)) ||
						((d.ImplementationFactory is not null || d.ImplementationInstance is not null) && d.ServiceType.IsAssignableFrom(implementationType))
					));

				if (foundSvcDescriptor is not null) {
#if DEBUG
					Console.WriteLine($"Found existing registered service ({foundSvcDescriptor.ServiceType.Name}) for {implementationType.Name}, and will use it!");
					Console.WriteLine($"	Tracking {foundSvcDescriptor.ServiceType.Name} for auto-initialization...");
#endif
					lock (AutoInitializeLock) {
						AutoInitializeServices.Add(foundSvcDescriptor.ServiceType);
					}

				} else {
#if DEBUG
					Console.WriteLine($"Scanning interfaces for: {implementationType.Name} ...");
#endif
					var primaryInterfaces = implementationType.GetImmediateInterfaces();
#if DEBUG
					foreach (var item in primaryInterfaces) {
						Console.WriteLine($"	{item.Name}");
					}
#endif
					var primaryInterface = FindPrimaryInterface(implementationType, serviceType, primaryInterfaces) ??
						throw new Exception(
							$"Cannot register {implementationType.Name} without a properly named primary interface. " +
							$"Candidates considered: {string.Join(", ", primaryInterfaces.Select(i => i.Name))}.");

#if DEBUG
					Console.WriteLine($"			{primaryInterface.Name} chosen as the service interface to register.");
#endif
					services.TryAdd(new ServiceDescriptor(
						primaryInterface,
						implementationType,
						serviceLifetime));

#if DEBUG
					Console.WriteLine($"	Tracking {primaryInterface.Name} for auto-initialization...");
#endif
					lock (AutoInitializeLock) {
						AutoInitializeServices.Add(primaryInterface);
					}

				}


			} else {
#if DEBUG
				Console.WriteLine($"Registering {implementationType.Name} as an {serviceType.Name} service.");
#endif
				services.TryAddEnumerable(new ServiceDescriptor(
					serviceType,
					implementationType,
					serviceLifetime));

			}

		}

		return services;

	}

	/// <summary>
	/// Chooses the interface a discovered <paramref name="implementationType"/> should be
	/// registered under: an interface named to match the class (e.g. <c>FooInitializer</c> →
	/// <c>IFooInitializer</c>), preferring one that itself derives from
	/// <paramref name="serviceType"/> (e.g. <c>IThemeMonitor : IAutoInitialize</c>) when both a
	/// derived and a non-derived candidate match by name.
	/// </summary>
	private static Type? FindPrimaryInterface(Type implementationType, Type serviceType, IReadOnlyCollection<Type> candidates) {

		bool NameMatches(Type i) => implementationType.Name.Contains(StrippedInterfaceName(i));

		var derivedFromMarker = candidates.Where(i => i != serviceType && serviceType.IsAssignableFrom(i));

		return derivedFromMarker.FirstOrDefault(NameMatches) ?? candidates.FirstOrDefault(NameMatches);

	}

	/// <summary>
	/// Strips the conventional leading <c>I</c> from an interface name (e.g.
	/// <c>IFooInitializer</c> → <c>FooInitializer</c>) for matching against an implementation's
	/// class name. Only strips the leading character — a mid-name capital <c>I</c> (e.g. the
	/// <c>I</c> in <c>Initializer</c>) is left alone, unlike a naive <c>Replace("I", "")</c>
	/// over the whole name.
	/// </summary>
	private static string StrippedInterfaceName(Type interfaceType) {
		var name = interfaceType.Name;
		return name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1])
			? name[1..]
			: name;
	}

	private static IEnumerable<Type> DiscoverServiceImplementations(Type serviceType) {

		var seenNames = new HashSet<string>();

		return AppDomain.CurrentDomain.GetAssemblies()
			.Where(IsNotExcluded)
			.Where(ReferencesMyLib)
			.Where(assembly => {
				var assemblyName = assembly.GetName()?.Name ?? string.Empty;
				return !string.IsNullOrEmpty(assemblyName) && seenNames.Add(assemblyName);
			})
			.SelectMany(assembly => {
				try {
					return ScanAssemblyForTypes(assembly, serviceType);
				} catch (Exception ex) when (
				  ex is ReflectionTypeLoadException ||
				  ex is FileNotFoundException ||
				  ex is BadImageFormatException) {
					// Log or handle the exception
					return [];
				}
			});

	}

	private static IEnumerable<Type> ScanAssemblyForTypes(Assembly assembly, Type serviceType) {
		try {
			return assembly
				.GetTypes()
				.Where(t => IsImplemented(serviceType, t));
		} catch (ReflectionTypeLoadException ex) {
			return ex.Types.Where(t => t is not null).Select(t => t!);
		} catch {
			return [];
		}
	}
	private static bool IsImplemented(Type serviceType, Type implementationType) =>
		implementationType.IsClass && serviceType.IsAssignableFrom(implementationType);
	private static bool ReferencesMyLib(Assembly assembly) =>
		assembly.GetReferencedAssemblies().Any(a => a.Name == ApplicationInitializer.LibName);
	private static HashSet<Type> GetImmediateInterfaces(this Type type) {
		var interfaces = type.GetInterfaces();
		var result = new HashSet<Type>(interfaces);
		foreach (var i in interfaces) {
			result.ExceptWith(i.GetInterfaces());
		}
		return result;
	}
	private static bool IsNotExcluded(Assembly assembly) {

		if (assembly?.IsDynamic != false || assembly.GetName()?.Name is not string name) {
			return false;
		}

		return !ExcludedAssemblyNames.Any(prefix =>
			name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

	}
	static readonly HashSet<string> ExcludedAssemblyNames = new(StringComparer.OrdinalIgnoreCase) {
		"System",
		"Microsoft",
		"mscorlib",
		"netstandard",
		"Azure",
		"Polly",
		"Humanizer",
		"SmartFormat",
		"Swashbuckle",
		"Google",
		"Grpc",
		"Facebook",
		"CommandLine",
		"McMaster",
		"Vio",
		"MediatR",
		"BlazorApplicationInsights"
	};

}

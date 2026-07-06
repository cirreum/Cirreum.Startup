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
	/// Resolves all the <see cref="IAutoInitialize"/> services tracked by this
	/// container's registration scan (ADR-0028).
	/// </summary>
	/// <param name="sp">The <see cref="IServiceProvider"/> that will provide the service implementations.</param>
	/// <returns>The materialized collection of services, in tracking order, deduplicated
	/// by instance. Empty when <c>AddApplicationInitializers</c> never ran for this
	/// container's collection.</returns>
	/// <remarks>
	/// Resolution is deliberately loud: auto-registration is best-effort (a manual app
	/// registration wins), but by resolution time every tracked type must produce an
	/// <see cref="IAutoInitialize"/> — a registration that was removed, or replaced with
	/// an implementation that doesn't implement the contract, fails here with an
	/// actionable error rather than being silently skipped. The deliberate opt-out is
	/// <see cref="ClearAutoInitializeServices"/>.
	/// </remarks>
	/// <exception cref="InvalidOperationException">A tracked service type no longer
	/// resolves, or resolves to an implementation that does not implement
	/// <see cref="IAutoInitialize"/>.</exception>
	public static IEnumerable<IAutoInitialize> GetAutoInitializeServices(this IServiceProvider sp) {
		ArgumentNullException.ThrowIfNull(sp);

		var manifest = sp.GetService<AutoInitializeManifest>();
		if (manifest is null) {
			return [];
		}

		var resolvedServices = new List<IAutoInitialize>();
		var seen = new HashSet<IAutoInitialize>(ReferenceEqualityComparer.Instance);
		foreach (var serviceType in manifest.Snapshot()) {

			var resolved = sp.GetService(serviceType)
				?? throw new InvalidOperationException(
					$"Auto-initialization tracked '{serviceType.FullName}', but it no longer resolves " +
					"from this container — its registration was removed after the registration scan. " +
					"Re-register it, or opt out of auto-initialization for this container via " +
					$"{nameof(ClearAutoInitializeServices)}().");

			if (resolved is not IAutoInitialize autoInitialize) {
				throw new InvalidOperationException(
					$"Auto-initialization tracked '{serviceType.FullName}', but it resolved to " +
					$"'{resolved.GetType().FullName}', which does not implement {nameof(IAutoInitialize)} — " +
					"its registration was replaced after the registration scan. Implement " +
					$"{nameof(IAutoInitialize)} on the replacement, or opt out for this container via " +
					$"{nameof(ClearAutoInitializeServices)}().");
			}

			if (seen.Add(autoInitialize)) {
				resolvedServices.Add(autoInitialize);
			}
		}

		return resolvedServices;
	}

	/// <summary>
	/// Clears the <see cref="IAutoInitialize"/> tracking for <em>this container</em> —
	/// the deliberate opt-out from auto-initialization. Registrations themselves are
	/// untouched; the services simply stop being auto-initialized.
	/// </summary>
	/// <param name="sp">The container whose tracking is cleared. Other containers in the
	/// same process are unaffected.</param>
	public static void ClearAutoInitializeServices(this IServiceProvider sp) {
		ArgumentNullException.ThrowIfNull(sp);
		sp.GetService<AutoInitializeManifest>()?.Clear();
	}


	private static IServiceCollection AddServicesFor<TServiceType>(
		this IServiceCollection services,
		ServiceLifetime serviceLifetime,
		bool isAutoInitService = false) {

		var serviceType = typeof(TServiceType);

		// Snapshot what the APP registered before this scan — pre-existing registrations
		// are deliberate composition and always win; only collisions the scan itself
		// creates are ambiguous (see RegisterAutoInitializeCandidate).
		HashSet<ServiceDescriptor>? preExisting = isAutoInitService ? [.. services] : null;

		foreach (var implementationType in DiscoverServiceImplementations(serviceType)) {

			if (isAutoInitService) {

				services.RegisterAutoInitializeCandidate(serviceType, implementationType, serviceLifetime, preExisting!);

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
	/// Try-first registration of a discovered auto-initialize implementation, plus
	/// tracking in this collection's <see cref="AutoInitializeManifest"/> (ADR-0028).
	/// An existing application registration of the implementation wins and is tracked
	/// as-is; otherwise the implementation registers under its primary interface.
	/// </summary>
	/// <param name="services">The service collection being composed.</param>
	/// <param name="serviceType">The auto-initialize marker (<see cref="IAutoInitialize"/>).</param>
	/// <param name="implementationType">The discovered implementation.</param>
	/// <param name="serviceLifetime">Lifetime for a registration this method creates.</param>
	/// <param name="preExisting">The descriptors that existed before the scan started —
	/// a pre-existing registration holding the primary interface is the application's
	/// deliberate choice and wins (the interface is still tracked; the loud resolution
	/// phase vets what it produces). Only a collision the scan itself created is
	/// ambiguous.</param>
	/// <exception cref="InvalidOperationException">The implementation's primary
	/// interface was claimed <em>during this same scan</em> by a different
	/// implementation that also implements the marker — an ambiguity <c>TryAdd</c>
	/// semantics cannot honor; the application must resolve it by registering its
	/// choice manually.</exception>
	internal static void RegisterAutoInitializeCandidate(
		this IServiceCollection services,
		Type serviceType,
		Type implementationType,
		ServiceLifetime serviceLifetime,
		IReadOnlySet<ServiceDescriptor> preExisting) {

		var manifest = GetOrAddManifest(services);

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
			manifest.Track(foundSvcDescriptor.ServiceType);
			return;
		}

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
		var existingForInterface = services.FirstOrDefault(d =>
			d.IsKeyedService is false && d.ServiceType == primaryInterface);

		if (existingForInterface is null) {
			services.TryAdd(new ServiceDescriptor(
				primaryInterface,
				implementationType,
				serviceLifetime));
		} else if (!preExisting.Contains(existingForInterface)
			&& existingForInterface.ImplementationType is { } existingImplementation
			&& serviceType.IsAssignableFrom(existingImplementation)) {
			// Two DISCOVERED implementations selected the same primary interface within
			// this scan — TryAdd semantics can honor only one, and scan order is not a
			// contract. Fail fast; the application resolves the ambiguity by registering
			// its choice manually (which the pre-existing paths then honor).
			throw new InvalidOperationException(
				$"Cannot auto-register {implementationType.FullName}: its primary interface " +
				$"{primaryInterface.FullName} is already registered to {existingImplementation.FullName}, " +
				$"which also implements {serviceType.Name}. Two auto-initialize implementations cannot " +
				"share a primary interface — register the one you want manually, before " +
				"AddApplicationInitializers, to resolve the ambiguity.");
		}
		// else: the interface is held by a PRE-EXISTING registration (the application's
		// deliberate choice — a different conforming implementation, a factory, or an
		// instance). Leave the registration alone and track the interface: a conforming
		// implementation initializes normally, and a non-conforming one fails loudly at
		// resolution.

#if DEBUG
		Console.WriteLine($"	Tracking {primaryInterface.Name} for auto-initialization...");
#endif
		manifest.Track(primaryInterface);
	}

	private static AutoInitializeManifest GetOrAddManifest(IServiceCollection services) {
		foreach (var descriptor in services) {
			if (descriptor.ServiceType == typeof(AutoInitializeManifest)
				&& descriptor.ImplementationInstance is AutoInitializeManifest existing) {
				return existing;
			}
		}
		var manifest = new AutoInitializeManifest();
		services.AddSingleton(manifest);
		return manifest;
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
			// The loadable subset still needs the implements-filter — returning it
			// unfiltered would register arbitrary types as initializers and fail the
			// provider build far from the cause.
			return ex.Types
				.Where(t => t is not null && IsImplemented(serviceType, t))
				.Select(t => t!);
		} catch {
			return [];
		}
	}
	private static bool IsImplemented(Type serviceType, Type implementationType) =>
		implementationType.IsClass
		&& !implementationType.IsAbstract
		// A generic type definition cannot be activated (and cannot register under a
		// non-generic service type) — a closed form must be registered manually.
		&& !implementationType.IsGenericTypeDefinition
		&& serviceType.IsAssignableFrom(implementationType);
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

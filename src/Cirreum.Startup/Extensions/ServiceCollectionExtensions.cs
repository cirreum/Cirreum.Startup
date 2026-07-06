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

				services.RegisterAutoInitializeCandidate(implementationType, serviceLifetime, preExisting!);

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

		if (isAutoInitService) {
			services.TrackMarkedRegistrations();
		}

		return services;

	}

	/// <summary>
	/// The second half of the auto-initialize contract: regardless of <em>how</em> a
	/// service was registered — auto-registered by the discovery scan above, or manually
	/// by the application — any registration that visibly carries the marker
	/// participates in initialization. The scan covers discoverable implementations;
	/// this sweep covers manual registrations the scan cannot see (closed generics,
	/// instances, marker-carrying service interfaces). It also enforces the single-slot
	/// cardinality rule for every tracked identity (ADR-0028).
	/// </summary>
	/// <remarks>
	/// <para>
	/// "Visibly" means the marker is provable at composition time: on the registered
	/// service type, on the descriptor's implementation type, or implemented by its
	/// instance. A factory registration whose service type does not carry the marker is
	/// unvettable here — place the marker on the service interface (or register the
	/// implementation type) for factory shapes to participate. Keyed and open-generic
	/// registrations are out of scope — they cannot be resolved by bare service type.
	/// </para>
	/// <para>
	/// <strong>Single-slot cardinality:</strong> an auto-initialized service identity
	/// must have exactly one registration — the one that resolves. Multiple marked
	/// implementations each get their own service identity and are resolved
	/// individually; set-style services (<c>IEnumerable&lt;T&gt;</c> consumption) are
	/// initialized by their owning host, never by auto-initialization. A tracked
	/// identity with more than one registration fails here, loudly, at composition time.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">A tracked service identity has more
	/// than one registration in this collection.</exception>
	internal static void TrackMarkedRegistrations(this IServiceCollection services) {

		var markerType = typeof(IAutoInitialize);

		var manifest = GetOrAddManifest(services);

		// One pass: count registrations per service type, and note every service type
		// whose registration visibly carries the marker.
		var registrationCounts = new Dictionary<Type, int>();
		var markedTypes = new HashSet<Type>();
		foreach (var descriptor in services) {
			if (descriptor.IsKeyedService
				|| descriptor.ServiceType.IsGenericTypeDefinition
				|| descriptor.ServiceType == typeof(AutoInitializeManifest)) {
				continue;
			}
			registrationCounts[descriptor.ServiceType] =
				registrationCounts.GetValueOrDefault(descriptor.ServiceType) + 1;
			var carriesMarker =
				markerType.IsAssignableFrom(descriptor.ServiceType)
				|| (descriptor.ImplementationType is { } implementationType && markerType.IsAssignableFrom(implementationType))
				|| (descriptor.ImplementationInstance is { } instance && markerType.IsInstanceOfType(instance));
			if (carriesMarker) {
				markedTypes.Add(descriptor.ServiceType);
			}
		}

		foreach (var markedType in markedTypes) {
#if DEBUG
			Console.WriteLine($"	Tracking {markedType.Name} for auto-initialization (marked registration)...");
#endif
			manifest.Track(markedType);
		}

		// Single-slot cardinality over EVERY tracked identity — including ones tracked
		// by the discovery paths above (e.g. a factory registration the sweep itself
		// cannot vet). Fail at capture, where the fix is a composition edit, rather
		// than initializing an arbitrary one (or all) of an ambiguous set.
		foreach (var trackedType in manifest.Snapshot()) {
			if (registrationCounts.GetValueOrDefault(trackedType) > 1) {
				throw new InvalidOperationException(
					$"Auto-initialization tracked '{trackedType.FullName}', but it has " +
					$"{registrationCounts[trackedType]} registrations in this container. " +
					"Auto-initialized services are single-slot: exactly one registration per " +
					"service identity. Give each marked implementation its own service identity " +
					"(its own interface, or a self-registration), or remove the extra " +
					"registrations. Set-style services consumed as IEnumerable<T> are " +
					"initialized by their owning host, not by auto-initialization.");
			}
		}
	}

	/// <summary>
	/// Try-first registration of a discovered auto-initialize implementation, plus
	/// tracking in this collection's <see cref="AutoInitializeManifest"/> (ADR-0028).
	/// An existing application registration of the implementation wins and is tracked
	/// as-is; otherwise the implementation registers under its primary interface.
	/// </summary>
	/// <param name="services">The service collection being composed.</param>
	/// <param name="implementationType">The discovered <see cref="IAutoInitialize"/>
	/// implementation. The service <em>identity</em> is never passed in — it is derived
	/// (the primary interface, by naming convention) or found (the try-first
	/// descriptor).</param>
	/// <param name="serviceLifetime">Lifetime for a registration this method creates.</param>
	/// <param name="preExisting">The descriptors that existed before the scan started —
	/// a pre-existing registration holding the primary interface is the application's
	/// deliberate composition and wins <em>when it provably auto-initializes</em>
	/// (quiet displacement: the slot still initializes, with the app's chosen
	/// implementation). Only a collision the scan itself created, or a pre-existing
	/// occupier that provably cannot initialize, is an error.</param>
	/// <exception cref="InvalidOperationException">The implementation's primary
	/// interface was claimed <em>during this same scan</em> by a different discovered
	/// implementation (ambiguity <c>TryAdd</c> cannot honor), OR it is held by a
	/// pre-existing registration that does <em>not</em> conform — the discovered
	/// implementation would be silently dropped and nothing would auto-initialize for
	/// the slot.</exception>
	internal static void RegisterAutoInitializeCandidate(
		this IServiceCollection services,
		Type implementationType,
		ServiceLifetime serviceLifetime,
		IReadOnlySet<ServiceDescriptor> preExisting) {

		var markerType = typeof(IAutoInitialize);
		var manifest = GetOrAddManifest(services);

		// Try-first: is the discovered implementation itself already registered? An
		// instance must actually BE the implementation (an instance of a DIFFERENT type
		// must not masquerade as it — that's a displacement, judged below). A factory is
		// opaque, so an assignable factory is presumed to produce the implementation and
		// the loud resolution phase verifies what it actually yields.
		var foundSvcDescriptor = services.FirstOrDefault(d =>
			d.IsKeyedService is false &&
			(
				(d.ImplementationType is not null && d.ImplementationType.Equals(implementationType)) ||
				(d.ImplementationInstance is not null && implementationType.IsInstanceOfType(d.ImplementationInstance)) ||
				(d.ImplementationFactory is not null && d.ServiceType.IsAssignableFrom(implementationType))
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
		var primaryInterface = FindPrimaryInterface(implementationType, markerType, primaryInterfaces) ??
			throw new InvalidOperationException(
				$"Cannot auto-register {implementationType.FullName}: no conventionally-named primary " +
				$"interface was found (for IMyService, the implementation should be named MyService). " +
				$"Candidates considered: {string.Join(", ", primaryInterfaces.Select(i => i.Name))}. " +
				"Register the service manually before AddApplicationInitializers() — the try-first " +
				"scan honors an existing registration and still auto-initializes it.");

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
		} else if (!preExisting.Contains(existingForInterface)) {
			// Claimed DURING this scan by a different discovered implementation —
			// TryAdd semantics can honor only one, and scan order is not a contract.
			// Fail fast; the application resolves the ambiguity by registering its
			// choice manually (which the pre-existing paths then honor).
			throw new InvalidOperationException(
				$"Cannot auto-register {implementationType.FullName}: its primary interface " +
				$"{primaryInterface.FullName} was claimed during this scan by " +
				$"{DescribeOccupier(existingForInterface)}, another discovered {markerType.Name} " +
				"implementation. Two auto-initialize implementations cannot share a primary " +
				"interface — register the one you want manually, before AddApplicationInitializers, " +
				"to resolve the ambiguity.");
		} else if (!OccupierProvablyConforms(existingForInterface, markerType, primaryInterface)) {
			// A PRE-EXISTING registration holds the interface but provably cannot
			// auto-initialize: the discovered implementation would be silently dropped
			// AND nothing would initialize for this slot. Intent is unknowable, but this
			// shape can never be the deliberate conforming-replacement case — loud.
			throw new InvalidOperationException(
				$"Cannot auto-register {implementationType.FullName}: its primary interface " +
				$"{primaryInterface.FullName} is already registered to " +
				$"{DescribeOccupier(existingForInterface)}, which does not implement " +
				$"{markerType.Name} — the discovered implementation would be silently dropped " +
				"and nothing would auto-initialize for this service. Remove or replace the " +
				$"conflicting registration, implement {markerType.Name} on it, or register " +
				$"{implementationType.Name} yourself under its own identity if both services " +
				"should exist.");
		}
		// else: a PRE-EXISTING registration that provably conforms — the application's
		// deliberate replacement of the discovered implementation. Quiet displacement:
		// the slot still auto-initializes, with the app's chosen implementation.

#if DEBUG
		Console.WriteLine($"	Tracking {primaryInterface.Name} for auto-initialization...");
#endif
		manifest.Track(primaryInterface);
	}

	/// <summary>
	/// Whether a pre-existing registration occupying a discovered implementation's
	/// primary interface is <em>provably</em> auto-initializable: the interface itself
	/// carries the marker (structural — every implementation conforms), or the
	/// descriptor's implementation type / instance does. Factories never reach this
	/// check — an assignable factory matches the try-first path and is verified at
	/// resolution instead.
	/// </summary>
	private static bool OccupierProvablyConforms(
		ServiceDescriptor occupier,
		Type markerType,
		Type primaryInterface) =>
		markerType.IsAssignableFrom(primaryInterface)
		|| (occupier.ImplementationType is { } implementationType && markerType.IsAssignableFrom(implementationType))
		|| (occupier.ImplementationInstance is { } instance && markerType.IsInstanceOfType(instance));

	private static string DescribeOccupier(ServiceDescriptor occupier) =>
		occupier.ImplementationType?.FullName
			?? occupier.ImplementationInstance?.GetType().FullName
			?? "a factory registration";

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

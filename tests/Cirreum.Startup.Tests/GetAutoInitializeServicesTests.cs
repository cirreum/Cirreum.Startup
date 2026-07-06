namespace Cirreum.Startup.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Integration tests for the container-scoped auto-initialize tracking (ADR-0028),
/// driven through the real registration scan: <c>AddApplicationInitializers()</c>
/// discovers this assembly's probe types. Locks multi-container isolation, per-container
/// Clear, tracking dedup, the loud resolution contract, and the abstract-type scan
/// exclusion.
/// </summary>
public class GetAutoInitializeServicesTests {

	private static ServiceProvider BuildWithInitializers(Action<IServiceCollection>? mutate = null) {
		var services = new ServiceCollection();
		services.AddApplicationInitializers();
		mutate?.Invoke(services);
		return services.BuildServiceProvider();
	}

	[Fact]
	public void DiscoversAndResolves_TrackedProbes_AsTheContainersOwnSingletons() {
		using var provider = BuildWithInitializers();

		var resolved = provider.GetAutoInitializeServices().ToList();

		resolved.OfType<ContainerProbe>().Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IContainerProbe>());
		resolved.OfType<GadgetProbe>().Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IGadgetProbe>());
	}

	[Fact]
	public void MultiContainer_TrackingAndInstances_AreFullyIsolated() {
		using var providerA = BuildWithInitializers();
		using var providerB = BuildWithInitializers();

		var probeA = providerA.GetAutoInitializeServices().OfType<ContainerProbe>().Single();
		var probeB = providerB.GetAutoInitializeServices().OfType<ContainerProbe>().Single();

		probeA.Should().NotBeSameAs(probeB);
	}

	[Fact]
	public void Clear_AffectsOnlyItsOwnContainer() {
		using var providerA = BuildWithInitializers();
		using var providerB = BuildWithInitializers();

		providerA.ClearAutoInitializeServices();

		providerA.GetAutoInitializeServices().Should().BeEmpty();
		providerB.GetAutoInitializeServices().Should().NotBeEmpty();
	}

	[Fact]
	public void AddApplicationInitializers_CalledTwice_TracksEachServiceOnce() {
		var services = new ServiceCollection();
		services.AddApplicationInitializers();
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		var resolved = provider.GetAutoInitializeServices().ToList();

		resolved.OfType<ContainerProbe>().Should().ContainSingle();
	}

	[Fact]
	public void RemovedRegistration_FailsLoudly_NamingTheTrackedType() {
		using var provider = BuildWithInitializers(services =>
			services.RemoveAll<IContainerProbe>());

		var act = () => provider.GetAutoInitializeServices();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(IContainerProbe).FullName}*no longer resolves*");
	}

	[Fact]
	public void ReplacedWithNonConformingImplementation_FailsLoudly() {
		using var provider = BuildWithInitializers(services =>
			services.Replace(ServiceDescriptor.Singleton<IGadgetProbe, PlainGadgetProbe>()));

		var act = () => provider.GetAutoInitializeServices();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(PlainGadgetProbe).FullName}*does not implement*");
	}

	[Fact]
	public void ContainerWithoutInitializers_ReturnsEmpty() {
		using var provider = new ServiceCollection().BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().BeEmpty();
		// And the opt-out is a safe no-op there too.
		provider.ClearAutoInitializeServices();
	}

	[Fact]
	public void ManualRegistration_InvisibleToTheScan_MarkerOnInterface_IsStillInitialized() {
		// The scan cannot see WidgetMonitorAlpha<T> (open generic), but the app
		// registered it manually and IWidgetMonitor carries the marker — purpose #2:
		// any marked registration participates, regardless of how it was registered.
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorAlpha<object>>();
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().OfType<WidgetMonitorAlpha<object>>()
			.Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IWidgetMonitor>());
	}

	[Fact]
	public void ManualInstanceRegistration_MarkerOnImplementationOnly_IsStillInitialized() {
		// Neither the service type (IScopedWidget) nor a scannable implementation
		// carries the marker — only the registered INSTANCE does. The sweep vets the
		// instance itself.
		var services = new ServiceCollection();
		var widget = new ScopedWidget<object>();
		services.AddSingleton<IScopedWidget>(widget);
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().Contain(widget);
	}

	[Fact]
	public void ManualFactoryRegistration_MarkerOnServiceInterface_IsStillInitialized() {
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor>(_ => new WidgetMonitorBeta<object>());
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().OfType<WidgetMonitorBeta<object>>()
			.Should().ContainSingle();
	}

	[Fact]
	public void ManualFactoryRegistration_MarkerNotVisibleAtComposition_IsNotTracked() {
		// The known, documented limit: a factory for a marker-less service type is
		// unvettable at composition time — it neither initializes nor fails the host.
		// Guidance: carry the marker on the service interface for factory shapes.
		var services = new ServiceCollection();
		services.AddSingleton<IScopedWidget>(_ => new ScopedWidget<object>());
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		var act = () => provider.GetAutoInitializeServices();

		act.Should().NotThrow();
		act().OfType<ScopedWidget<object>>().Should().BeEmpty();
	}

	[Fact]
	public void SingleSlot_InterfaceCarriedMarker_TwoRegistrations_FailsLoudlyAtCapture() {
		// IWidgetMonitor : IAutoInitialize declares a single-slot lifecycle contract;
		// two registrations of it is a composition error, caught at capture — not
		// last-one-wins, not initialize-both.
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorAlpha<object>>();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorBeta<object>>();

		var act = () => services.AddApplicationInitializers();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(IWidgetMonitor).FullName}*single-slot*");
	}

	[Fact]
	public void SingleSlot_MarkedRegistrationShadowedByAnother_FailsLoudlyAtCapture() {
		// A marked instance shadowed by a later unmarked registration: the marked
		// service could never be reached by single resolution — loud, not silent.
		var services = new ServiceCollection();
		services.AddSingleton<IScopedWidget>(new ScopedWidget<object>());
		services.AddSingleton<IScopedWidget, PlainScopedWidget>();

		var act = () => services.AddApplicationInitializers();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(IScopedWidget).FullName}*single-slot*");
	}

	[Fact]
	public void SingleSlot_MultipleMarkedImplementations_EachUnderItsOwnIdentity_AllInitialize() {
		// The sanctioned shape for "multiple services, all auto-initialized": each
		// marked implementation has its own service identity and is resolved
		// individually.
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorAlpha<object>>();
		var scopedWidget = new ScopedWidget<object>();
		services.AddSingleton<IScopedWidget>(scopedWidget);
		services.AddApplicationInitializers();
		using var provider = services.BuildServiceProvider();

		var resolved = provider.GetAutoInitializeServices().ToList();

		resolved.OfType<WidgetMonitorAlpha<object>>().Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IWidgetMonitor>());
		resolved.Should().Contain(scopedWidget);
	}

	[Fact]
	public void AbstractImplementations_AreExcludedFromTheScan() {
		var services = new ServiceCollection();

		// Before the IsAbstract fix, discovery selected AbstractAutoInitializeBase and
		// this call failed for every test in this assembly.
		services.AddApplicationInitializers();

		services.Should().NotContain(d => d.ImplementationType == typeof(AbstractAutoInitializeBase));
	}

}

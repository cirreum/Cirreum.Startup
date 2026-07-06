namespace Cirreum.Startup.Tests;

using Cirreum.Startup;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Seam tests for <c>RegisterAutoInitializeCandidate</c> — the per-implementation
/// try-first registration + tracking step, exercised directly so collision scenarios
/// don't have to exist as discoverable types (which would poison every scan-driven
/// test in this assembly). Uses closed forms of open-generic implementations, which
/// the real scan cannot see.
/// </summary>
public class RegisterAutoInitializeCandidateTests {

	private static readonly HashSet<ServiceDescriptor> NothingPreExisting = [];

	[Fact]
	public void TwoDiscoveredImplementations_SharingAPrimaryInterface_FailFast() {
		var services = new ServiceCollection();
		services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, NothingPreExisting);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorBeta<object>), ServiceLifetime.Singleton, NothingPreExisting);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*cannot share a primary interface*");
	}

	[Fact]
	public void PreExistingRegistrationOfTheSameImplementation_Wins_AndIsTracked() {
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorAlpha<object>>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);
		using var provider = services.BuildServiceProvider();

		var resolved = provider.GetAutoInitializeServices().ToList();
		resolved.Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IWidgetMonitor>());
	}

	[Fact]
	public void ConformingDisplacement_InterfaceCarriedMarker_Quiet_TheChosenImplInitializes() {
		// The application deliberately chose Beta for the interface Alpha would have
		// claimed. IWidgetMonitor carries the marker, so the occupier conforms
		// STRUCTURALLY — quiet displacement: Alpha is never registered (not part of the
		// app), the container holds exactly ONE registration of IWidgetMonitor, and
		// Beta (the app's chosen implementation) is what initializes.
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorBeta<object>>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);

		act.Should().NotThrow();
		// Alpha was displaced, not added — the roster is registrations, and Alpha isn't one.
		services.Should().NotContain(d => d.ImplementationType == typeof(WidgetMonitorAlpha<object>));
		using var provider = services.BuildServiceProvider();
		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeOfType<WidgetMonitorBeta<object>>();
	}

	[Fact]
	public void ConformingDisplacement_ImplCarriedMarker_Quiet_TheChosenImplInitializes() {
		// The clean-interface variant: the occupier itself carries the marker, so the
		// slot provably initializes with the app's choice — quiet displacement.
		var services = new ServiceCollection();
		services.AddSingleton<IGadgetProbe, GadgetProbeAlternate<object>>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);

		act.Should().NotThrow();
		using var provider = services.BuildServiceProvider();
		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeOfType<GadgetProbeAlternate<object>>();
	}

	[Fact]
	public void NonConformingOccupier_FailsLoudlyAtScan() {
		// The dangerous displacement: the occupier can't initialize, so the discovered
		// implementation would be silently dropped AND nothing would initialize for the
		// slot. Loud, at scan — with the remedies in the message.
		var services = new ServiceCollection();
		services.AddSingleton<IGadgetProbe, PlainGadgetProbe>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(PlainGadgetProbe).FullName}*does not implement*");
	}

	[Fact]
	public void NonConformingInstanceOccupier_DoesNotMatchTryFirst_FailsLoudlyAtScan() {
		// The instance-leg tightening: an instance of a DIFFERENT type must not
		// masquerade as "the discovered implementation is registered" — it routes to
		// the displacement gate, where a non-conforming instance is loud.
		var services = new ServiceCollection();
		services.AddSingleton<IGadgetProbe>(new PlainGadgetProbe());
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(PlainGadgetProbe).FullName}*does not implement*");
	}

	[Fact]
	public void InstanceOfTheCandidateItself_TryFirstHonorsIt() {
		var services = new ServiceCollection();
		var instance = new GadgetProbe();
		services.AddSingleton<IGadgetProbe>(instance);
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);

		act.Should().NotThrow();
		using var provider = services.BuildServiceProvider();
		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeSameAs(instance);
	}

	[Fact]
	public void PreExistingFactory_ProducingANonConformingInstance_FailsLoudlyAtResolution() {
		// Registration cannot vet a factory; the loud resolution phase renders the
		// verdict per the try-register / resolve-loudly contract.
		var services = new ServiceCollection();
		services.AddSingleton<IGadgetProbe>(_ => new PlainGadgetProbe());
		var preExisting = new HashSet<ServiceDescriptor>(services);

		services.RegisterAutoInitializeCandidate(
			typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);
		using var provider = services.BuildServiceProvider();

		var act = () => provider.GetAutoInitializeServices();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*does not implement*");
	}

	[Fact]
	public void PreExistingFactory_ProducingAConformingInstance_InitializesNormally() {
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor>(_ => new WidgetMonitorBeta<object>());
		var preExisting = new HashSet<ServiceDescriptor>(services);

		services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeOfType<WidgetMonitorBeta<object>>();
	}

	[Fact]
	public void TrackingTheSameServiceTypeTwice_ResolvesOnce() {
		var services = new ServiceCollection();
		services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, NothingPreExisting);
		var afterFirst = new HashSet<ServiceDescriptor>(services);
		services.RegisterAutoInitializeCandidate(
			typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, afterFirst);
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().ContainSingle();
	}

}

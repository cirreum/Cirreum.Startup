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
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, NothingPreExisting);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(WidgetMonitorBeta<object>), ServiceLifetime.Singleton, NothingPreExisting);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*cannot share a primary interface*");
	}

	[Fact]
	public void PreExistingRegistrationOfTheSameImplementation_Wins_AndIsTracked() {
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorAlpha<object>>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);
		using var provider = services.BuildServiceProvider();

		var resolved = provider.GetAutoInitializeServices().ToList();
		resolved.Should().ContainSingle()
			.Which.Should().BeSameAs(provider.GetRequiredService<IWidgetMonitor>());
	}

	[Fact]
	public void PreExistingRegistrationOfADifferentConformingImplementation_Wins_NoCollision() {
		// The application deliberately chose Beta for the interface Alpha would have
		// claimed — try-first honors that choice, tracks the interface, and Beta (the
		// app's active implementation) is what initializes.
		var services = new ServiceCollection();
		services.AddSingleton<IWidgetMonitor, WidgetMonitorBeta<object>>();
		var preExisting = new HashSet<ServiceDescriptor>(services);

		var act = () => services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);

		act.Should().NotThrow();
		using var provider = services.BuildServiceProvider();
		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeOfType<WidgetMonitorBeta<object>>();
	}

	[Fact]
	public void PreExistingFactory_ProducingANonConformingInstance_FailsLoudlyAtResolution() {
		// Registration cannot vet a factory; the loud resolution phase renders the
		// verdict per the try-register / resolve-loudly contract.
		var services = new ServiceCollection();
		services.AddSingleton<IGadgetProbe>(_ => new PlainGadgetProbe());
		var preExisting = new HashSet<ServiceDescriptor>(services);

		services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(GadgetProbe), ServiceLifetime.Singleton, preExisting);
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
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, preExisting);
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().ContainSingle()
			.Which.Should().BeOfType<WidgetMonitorBeta<object>>();
	}

	[Fact]
	public void TrackingTheSameServiceTypeTwice_ResolvesOnce() {
		var services = new ServiceCollection();
		services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, NothingPreExisting);
		var afterFirst = new HashSet<ServiceDescriptor>(services);
		services.RegisterAutoInitializeCandidate(
			typeof(IAutoInitialize), typeof(WidgetMonitorAlpha<object>), ServiceLifetime.Singleton, afterFirst);
		using var provider = services.BuildServiceProvider();

		provider.GetAutoInitializeServices().Should().ContainSingle();
	}

}

namespace Cirreum.Startup.Tests;

using Cirreum.Startup;
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
			services.RemoveAll(typeof(IContainerProbe)));

		var act = () => provider.GetAutoInitializeServices();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{typeof(IContainerProbe).FullName}*no longer resolves*");
	}

	[Fact]
	public void ReplacedWithNonConformingImplementation_FailsLoudly() {
		using var provider = BuildWithInitializers(services =>
			services.Replace(ServiceDescriptor.Singleton(typeof(IGadgetProbe), typeof(PlainGadgetProbe))));

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
	public void AbstractImplementations_AreExcludedFromTheScan() {
		var services = new ServiceCollection();

		// Before the IsAbstract fix, discovery selected AbstractAutoInitializeBase and
		// this call failed for every test in this assembly.
		services.AddApplicationInitializers();

		services.Should().NotContain(d => d.ImplementationType == typeof(AbstractAutoInitializeBase));
	}

}

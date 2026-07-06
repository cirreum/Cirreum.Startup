namespace Cirreum.Startup.Tests;

using Cirreum.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// End-to-end tests through the real <see cref="ApplicationInitializer"/> for the
/// original IAutoInitialize use case: an existing interface/impl service pair, marker
/// on the implementation only, <c>InitializeAsync</c> explicitly implemented so the
/// consumer-facing interface stays clean — auto-registered, resolved through its
/// service interface, and actually initialized at startup.
/// </summary>
public class ApplicationInitializerEndToEndTests {

	private static (ApplicationInitializer Initializer, ServiceProvider Provider) CreateInitialized() {
		var services = new ServiceCollection();
		services.AddApplicationInitializers();
		var provider = services.BuildServiceProvider();
		return (new ApplicationInitializer(
			NullLogger<ApplicationInitializer>.Instance, provider), provider);
	}

	[Fact]
	public async Task ExplicitlyImplementedInitializer_IsDiscovered_Registered_AndInvokedOnce() {
		var (initializer, provider) = CreateInitialized();
		using var _ = provider;

		await initializer.InitializeApplication();

		// Registered under its clean service interface, and the EXPLICIT
		// IAutoInitialize.InitializeAsync ran exactly once.
		var service = provider.GetRequiredService<IMyCoolService>();
		service.Should().BeOfType<MyCoolService>()
			.Which.InitializeCount.Should().Be(1);
	}

	[Fact]
	public void ExplicitImplementation_KeepsInitializeAsync_OffTheConsumerSurface() {
		// The point of the pattern: consumers of IMyCoolService (and of the concrete
		// type's public surface) never see InitializeAsync.
		typeof(IMyCoolService).GetMethod(nameof(IAutoInitialize.InitializeAsync)).Should().BeNull();
		typeof(MyCoolService).GetMethod(nameof(IAutoInitialize.InitializeAsync)).Should().BeNull();
	}

	[Fact]
	public async Task InitializeApplication_RunTwice_DoesNotReinitialize() {
		var (initializer, provider) = CreateInitialized();
		using var _ = provider;

		await initializer.InitializeApplication();
		await initializer.InitializeApplication();

		// The post-run Clear keeps a second pass a no-op — parity with the
		// pre-ADR-0028 behavior, now per-container.
		provider.GetRequiredService<IMyCoolService>()
			.Should().BeOfType<MyCoolService>()
			.Which.InitializeCount.Should().Be(1);
	}

}

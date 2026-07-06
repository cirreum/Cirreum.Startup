namespace Cirreum.Startup.Tests;

using Cirreum.Startup;

// Test doubles for the auto-initialize tracking tests. The real assembly scan runs in
// every integration test and discovers EVERY concrete, non-abstract IAutoInitialize
// implementer in this assembly — so types meant for discovery are plain classes, and
// types that must stay OUT of the scan are either open generics (IsAssignableFrom is
// false for a generic type definition) or don't implement IAutoInitialize at all.

/// <summary>Discovered by the scan in every integration test; primary interface derives the marker.</summary>
internal interface IContainerProbe : IAutoInitialize;

/// <inheritdoc cref="IContainerProbe"/>
internal sealed class ContainerProbe : IContainerProbe {
	public int InitializeCount;
	public ValueTask InitializeAsync() {
		this.InitializeCount++;
		return ValueTask.CompletedTask;
	}
}

/// <summary>Discovered; its primary interface does NOT derive the marker, so a
/// replacement registration can be non-conforming.</summary>
internal interface IGadgetProbe;

/// <inheritdoc cref="IGadgetProbe"/>
internal sealed class GadgetProbe : IGadgetProbe, IAutoInitialize {
	public ValueTask InitializeAsync() => ValueTask.CompletedTask;
}

/// <summary>Implements the probe interface but NOT IAutoInitialize — invisible to the
/// scan; used as a non-conforming replacement/factory product.</summary>
internal sealed class PlainGadgetProbe : IGadgetProbe;

/// <summary>Abstract implementer — the scan must ignore it. Its presence in this
/// assembly is itself a regression guard: before the IsAbstract fix, discovery
/// selected it and every AddApplicationInitializers call failed.</summary>
internal abstract class AbstractAutoInitializeBase : IAutoInitialize {
	public abstract ValueTask InitializeAsync();
}

/// <summary>Sweep-test family: the interface does NOT carry the marker and its only
/// implementation is an open generic (scan-invisible), so nothing but the
/// marked-registration sweep can ever track it.</summary>
internal interface IScopedWidget;

/// <inheritdoc cref="IScopedWidget"/>
internal sealed class ScopedWidget<T> : IScopedWidget, IAutoInitialize {
	public ValueTask InitializeAsync() => ValueTask.CompletedTask;
}

/// <summary>Seam-test family: open generics are invisible to the scan, so the closed
/// forms can be fed to RegisterAutoInitializeCandidate directly without poisoning the
/// discovery-driven tests.</summary>
internal interface IWidgetMonitor : IAutoInitialize;

/// <inheritdoc cref="IWidgetMonitor"/>
internal sealed class WidgetMonitorAlpha<T> : IWidgetMonitor {
	public ValueTask InitializeAsync() => ValueTask.CompletedTask;
}

/// <inheritdoc cref="IWidgetMonitor"/>
internal sealed class WidgetMonitorBeta<T> : IWidgetMonitor {
	public ValueTask InitializeAsync() => ValueTask.CompletedTask;
}

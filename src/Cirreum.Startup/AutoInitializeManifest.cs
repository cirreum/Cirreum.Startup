namespace Cirreum.Startup;

/// <summary>
/// Per-container record of the service types tracked for auto-initialization
/// (ADR-0028). Registered as a singleton <em>instance</em> into the same
/// <c>IServiceCollection</c> that <c>AddApplicationInitializers</c> composes, so every
/// container sees exactly its own tracked types and the manifest dies with its
/// container — no process-global state, no cross-host bleed.
/// </summary>
internal sealed class AutoInitializeManifest {

	private readonly Lock _lock = new();
	private readonly List<Type> _serviceTypes = [];

	/// <summary>
	/// Tracks a service type for auto-initialization. Tracking the same type twice is a
	/// no-op — re-running the registration scan must not double-initialize a service.
	/// </summary>
	public void Track(Type serviceType) {
		ArgumentNullException.ThrowIfNull(serviceType);
		lock (this._lock) {
			if (!this._serviceTypes.Contains(serviceType)) {
				this._serviceTypes.Add(serviceType);
			}
		}
	}

	/// <summary>Returns a point-in-time copy of the tracked service types.</summary>
	public Type[] Snapshot() {
		lock (this._lock) {
			return [.. this._serviceTypes];
		}
	}

	/// <summary>Clears this container's tracked service types.</summary>
	public void Clear() {
		lock (this._lock) {
			this._serviceTypes.Clear();
		}
	}

}

namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// Which of the eight areas the issue's contract suite names a check belongs to. Encoded in every check's
/// id as the <c>MDC&lt;nn&gt;</c> two-digit prefix - see <see cref="IConformanceCheck.Id" /> - so a check's
/// category is always readable from its id alone.
/// </summary>
public enum ConformanceCategory
{
	/// <summary>MDC01xx - manifest and identifier rules.</summary>
	ManifestAndIdentifiers = 1,

	/// <summary>MDC02xx - registration and version negotiation.</summary>
	RegistrationAndNegotiation = 2,

	/// <summary>MDC03xx - capability serialization.</summary>
	CapabilitySerialization = 3,

	/// <summary>MDC04xx - duplicate ids.</summary>
	DuplicateIds = 4,

	/// <summary>MDC05xx - timeout and cancellation compliance.</summary>
	TimeoutAndCancellation = 5,

	/// <summary>MDC06xx - disconnect and reconnect behaviour.</summary>
	DisconnectAndReconnect = 6,

	/// <summary>MDC07xx - health endpoint behaviour.</summary>
	HealthEndpoint = 7,

	/// <summary>MDC08xx - bounded logging, event and variable queues.</summary>
	BoundedQueues = 8
}

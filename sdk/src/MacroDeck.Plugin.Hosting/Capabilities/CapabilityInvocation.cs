using System.Text.Json;

namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>One request to run something the plugin declared.</summary>
public sealed class CapabilityInvocation
{
	/// <summary>The capability kind, one of <c>CapabilityKinds</c>.</summary>
	public required string Kind { get; init; }

	/// <summary>The declared local id being invoked, unqualified.</summary>
	public required string LocalId { get; init; }

	/// <summary>What to do with it. Defined per capability kind.</summary>
	public required string Operation { get; init; }

	/// <summary>Operation arguments, or <c>null</c> when there are none.</summary>
	public JsonElement? Arguments { get; init; }

	/// <summary>The id of the <c>capability.invoke</c> this answers.</summary>
	public required string CorrelationId { get; init; }

	/// <summary>Present when the caller intends the invocation to be safely retryable.</summary>
	public string? IdempotencyKey { get; init; }

	/// <summary>When the caller stops waiting, when it said so.</summary>
	public DateTimeOffset? Deadline { get; init; }

	/// <summary>
	/// The dependency injection scope created for this invocation. Resolve scoped services from here
	/// rather than from the root provider, so per-invocation state is disposed with the invocation.
	/// </summary>
	public required IServiceProvider Services { get; init; }
}

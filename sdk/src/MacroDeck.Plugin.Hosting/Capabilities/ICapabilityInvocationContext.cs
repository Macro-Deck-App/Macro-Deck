namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>
/// The invocation a scoped service is running inside, injectable from anywhere in the per-invocation
/// scope.
///
/// <para>
/// It exists so a service several constructor hops away from the handler can know which capability it
/// is serving without that being threaded through every signature - the ambient-request pattern
/// <c>IHttpContextAccessor</c> serves in ASP.NET Core, minus the accessor, because the scope is
/// already per-invocation and nothing here needs an async-local.
/// </para>
/// </summary>
public interface ICapabilityInvocationContext
{
	/// <summary>The capability kind being invoked.</summary>
	string Kind { get; }

	/// <summary>The declared local id being invoked.</summary>
	string LocalId { get; }

	/// <summary>What is being done with it.</summary>
	string Operation { get; }

	/// <summary>The id of the <c>capability.invoke</c> being answered.</summary>
	string CorrelationId { get; }

	/// <summary>When the caller stops waiting, when it said so.</summary>
	DateTimeOffset? Deadline { get; }

	/// <summary>Cancelled when the invocation is withdrawn, times out, or the session ends.</summary>
	CancellationToken CancellationToken { get; }
}

internal sealed class CapabilityInvocationContext : ICapabilityInvocationContext
{
	public required string Kind { get; init; }

	public required string LocalId { get; init; }

	public required string Operation { get; init; }

	public required string CorrelationId { get; init; }

	public DateTimeOffset? Deadline { get; init; }

	public required CancellationToken CancellationToken { get; init; }
}

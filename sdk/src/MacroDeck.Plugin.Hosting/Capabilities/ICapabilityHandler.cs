using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>
/// Everything one capability kind needs to do: say what it offers, and run an invocation.
///
/// <para>
/// This is the whole extension point. The transport, the dispatcher, the catalogue and the validator
/// know nothing about any particular kind, so adding one - events, variables, weather - is a new
/// implementation of this interface and a registration, never a change to the plumbing.
/// </para>
/// </summary>
public interface ICapabilityHandler
{
	/// <summary>Which kind this handler serves. One of <see cref="CapabilityKinds" />.</summary>
	string Kind { get; }

	/// <summary>
	/// The capabilities this handler offers right now.
	///
	/// <para>
	/// Called before the plugin has initialized anything and possibly again mid-session, so it must be
	/// side-effect free and must not require a live connection to whatever it fronts - the same rule
	/// an in-process integration's declarations follow.
	/// </para>
	/// </summary>
	IReadOnlyList<DeclaredCapability> DeclareCapabilities();

	/// <summary>
	/// Runs one invocation. Failures are returned, not thrown - though a thrown exception is caught,
	/// redacted and reported as an internal error rather than tearing down the session.
	/// </summary>
	Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken);
}

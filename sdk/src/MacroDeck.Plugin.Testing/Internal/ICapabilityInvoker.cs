namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>
/// Whatever can run a capability invocation and hand back the outcome - <see cref="PluginSessionView" />
/// over the wire, <see cref="PluginTestHarness" /> with no transport at all. The typed capability
/// clients (<see cref="ActionsTestClient" /> and the rest) are written against this rather than either
/// concrete type, so the same client type works unmodified on both.
/// </summary>
internal interface ICapabilityInvoker
{
	/// <summary>Same contract as <see cref="PluginSessionView.InvokeAsync" />.</summary>
	Task<CapabilityInvocationOutcome> InvokeAsync(
		string kind,
		string localId,
		string operation,
		object? arguments = null,
		CapabilityInvokeOptions? options = null);
}

using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>events</c> capability. This is the capability a host uses to ask what a plugin's events look
/// like; a plugin raising an occurrence sends <c>event.publish</c> directly, which is what
/// <see cref="MacroDeckTestHost.Events" /> collects - there is no invocation for that half.
/// </summary>
public sealed class EventsTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal EventsTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The full event catalogue this plugin declares.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Events,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Events.Describe,
			null,
			options);

	/// <summary>Asks a dynamic event-options provider for one event's current options.</summary>
	public Task<CapabilityInvocationOutcome> GetOptionsAsync(EventOptionsArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Events,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Events.Options,
			arguments,
			options);
}

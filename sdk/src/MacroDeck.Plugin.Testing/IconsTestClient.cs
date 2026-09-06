using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>icons</c> capability.</summary>
public sealed class IconsTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal IconsTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The icons this plugin declares.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Icons,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Icons.Describe,
			null,
			options);
}

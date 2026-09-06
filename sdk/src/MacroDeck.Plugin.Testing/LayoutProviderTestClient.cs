using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>layout-provider</c> capability.</summary>
public sealed class LayoutProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal LayoutProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.LayoutProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.LayoutProvider.Describe,
			null,
			options);

	/// <summary>The layouts the provider currently offers.</summary>
	public Task<CapabilityInvocationOutcome> GetLayoutsAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.LayoutProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.LayoutProvider.Layouts,
			null,
			options);
}

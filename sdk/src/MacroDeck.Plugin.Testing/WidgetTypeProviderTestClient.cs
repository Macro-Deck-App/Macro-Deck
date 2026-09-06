using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>widget-type-provider</c> capability.</summary>
public sealed class WidgetTypeProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal WidgetTypeProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.WidgetTypeProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.WidgetTypeProvider.Describe,
			null,
			options);

	/// <summary>The widget types the provider currently offers.</summary>
	public Task<CapabilityInvocationOutcome> GetWidgetTypesAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.WidgetTypeProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.WidgetTypeProvider.WidgetTypes,
			null,
			options);
}

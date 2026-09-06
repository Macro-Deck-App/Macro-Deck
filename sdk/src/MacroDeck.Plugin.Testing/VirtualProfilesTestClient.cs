using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>virtual-profiles</c> capability.</summary>
public sealed class VirtualProfilesTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal VirtualProfilesTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.Describe,
			null,
			options);

	/// <summary>The virtual profiles currently available.</summary>
	public Task<CapabilityInvocationOutcome> GetProfilesAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.Profiles,
			null,
			options);

	/// <summary>Forwards a widget interaction on a virtual profile's folder.</summary>
	public Task<CapabilityInvocationOutcome> SendWidgetInteractionAsync(
		WidgetInteractionArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.VirtualProfiles,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.VirtualProfiles.WidgetInteraction,
			arguments,
			options);
}

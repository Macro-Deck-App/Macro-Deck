using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>screensaver-provider</c> capability.</summary>
public sealed class ScreenSaverProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal ScreenSaverProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ScreenSaverProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ScreenSaverProvider.Describe,
			null,
			options);

	/// <summary>The screensavers the provider currently offers.</summary>
	public Task<CapabilityInvocationOutcome> GetScreenSaversAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ScreenSaverProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ScreenSaverProvider.ScreenSavers,
			null,
			options);
}

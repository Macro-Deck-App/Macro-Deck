using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>folder-view-provider</c> capability.</summary>
public sealed class FolderViewProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal FolderViewProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.FolderViewProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.FolderViewProvider.Describe,
			null,
			options);

	/// <summary>The folder views the provider currently offers.</summary>
	public Task<CapabilityInvocationOutcome> GetFolderViewsAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.FolderViewProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.FolderViewProvider.FolderViews,
			null,
			options);
}

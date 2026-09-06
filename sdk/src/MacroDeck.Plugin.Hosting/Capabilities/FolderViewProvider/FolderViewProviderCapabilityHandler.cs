using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeck.Plugin.Hosting.Capabilities.FolderViewProvider;

/// <summary>
/// Exposes every registered integration's <c>IFolderViewProvider</c> as the <c>folder-view-provider</c>
/// capability. Provider-shaped like <c>layout-provider</c>: one <c>provider</c> local id. Registration is
/// driven by the provider itself over the <c>folder-views</c> host api, so the host-to-plugin direction
/// only describes the provider and re-reads its catalog.
/// </summary>
internal sealed class FolderViewProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IFolderViewProvider> _providers =
		[.. integrations.OfType<IFolderViewProvider>()];

	public string Kind => CapabilityKinds.FolderViewProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.FolderViewProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see LayoutProviderCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation,
			CapabilityOperations.FolderViewProvider.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new FolderViewProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
					metadata.Name,
				FolderViews = FolderViews()
			}));
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No folder view provider '{invocation.LocalId}' is registered in this plugin."));
		}

		if (string.Equals(invocation.Operation,
			CapabilityOperations.FolderViewProvider.FolderViews,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new FolderViewProviderFolderViewsResult
				{ FolderViews = FolderViews() }));
		}

		return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
			$"The folder-view-provider capability has no operation '{invocation.Operation}'."));
	}

	private IReadOnlyList<FolderViewDescriptorDto> FolderViews()
		=> [.. _providers.SelectMany(provider => provider.GetFolderViews()).Select(FolderViewDescriptorMapper.ToDto)];
}

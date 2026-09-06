using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;

/// <summary>
/// Exposes every registered integration's <c>ILayoutProvider</c> as the <c>layout-provider</c>
/// capability. Provider-shaped like <c>device-provider</c>: one <c>provider</c> local id. Registration
/// is driven by the provider itself over the <c>layouts</c> host api, so the host-to-plugin direction
/// only describes the provider and re-reads its catalogue.
/// </summary>
internal sealed class LayoutProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<ILayoutProvider> _providers = [.. integrations.OfType<ILayoutProvider>()];

	public string Kind => CapabilityKinds.LayoutProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.LayoutProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see DeviceProviderCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation,
			CapabilityOperations.LayoutProvider.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new LayoutProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
					metadata.Name,
				Layouts = Layouts()
			}));
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No layout provider '{invocation.LocalId}' is registered in this plugin."));
		}

		if (string.Equals(invocation.Operation, CapabilityOperations.LayoutProvider.Layouts, StringComparison.Ordinal))
		{
			return Task.FromResult(
				CapabilityInvocationResult.Ok(new LayoutProviderLayoutsResult { Layouts = Layouts() }));
		}

		return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
			$"The layout-provider capability has no operation '{invocation.Operation}'."));
	}

	private IReadOnlyList<LayoutDescriptorDto> Layouts()
		=> [.. _providers.SelectMany(provider => provider.GetLayouts()).Select(LayoutDescriptorMapper.ToDto)];
}

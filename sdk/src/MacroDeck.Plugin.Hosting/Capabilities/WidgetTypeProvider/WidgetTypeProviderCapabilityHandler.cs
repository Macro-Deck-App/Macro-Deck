using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Capabilities.WidgetTypeProvider;

/// <summary>
/// Exposes every registered integration's <c>IWidgetTypeProvider</c> as the <c>widget-type-provider</c>
/// capability. Provider-shaped like <c>layout-provider</c>: one <c>provider</c> local id. Registration is
/// driven by the provider itself over the <c>widget-types</c> host api, so the host-to-plugin direction
/// only describes the provider and re-reads its catalog.
/// </summary>
internal sealed class WidgetTypeProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IWidgetTypeProvider> _providers =
		[.. integrations.OfType<IWidgetTypeProvider>()];

	public string Kind => CapabilityKinds.WidgetTypeProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.WidgetTypeProvider, LocalId = ProviderCapabilityId.LocalId,
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
			CapabilityOperations.WidgetTypeProvider.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new WidgetTypeProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
					metadata.Name,
				WidgetTypes = WidgetTypes()
			}));
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No widget type provider '{invocation.LocalId}' is registered in this plugin."));
		}

		if (string.Equals(invocation.Operation,
			CapabilityOperations.WidgetTypeProvider.WidgetTypes,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new WidgetTypeProviderWidgetTypesResult
				{ WidgetTypes = WidgetTypes() }));
		}

		return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
			$"The widget-type-provider capability has no operation '{invocation.Operation}'."));
	}

	private IReadOnlyList<WidgetTypeDescriptorDto> WidgetTypes()
		=> [.. _providers.SelectMany(provider => provider.GetWidgetTypes()).Select(WidgetTypeDescriptorMapper.ToDto)];
}

using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ScreenSaverProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeck.Plugin.Hosting.Capabilities.ScreenSaverProvider;

internal sealed class ScreenSaverProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IScreenSaverProvider> _providers =
		[.. integrations.OfType<IScreenSaverProvider>()];

	public string Kind => CapabilityKinds.ScreenSaverProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.ScreenSaverProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (string.Equals(invocation.Operation,
			CapabilityOperations.ScreenSaverProvider.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new ScreenSaverProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
					metadata.Name,
				ScreenSavers = ScreenSavers()
			}));
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No screensaver provider '{invocation.LocalId}' is registered in this plugin."));
		}

		if (string.Equals(invocation.Operation,
			CapabilityOperations.ScreenSaverProvider.ScreenSavers,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new ScreenSaverProviderScreenSaversResult
				{ ScreenSavers = ScreenSavers() }));
		}

		return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
			$"The screensaver-provider capability has no operation '{invocation.Operation}'."));
	}

	private IReadOnlyList<ScreenSaverDescriptorDto> ScreenSavers()
		=> [.. _providers.SelectMany(provider => provider.GetScreenSavers()).Select(ScreenSaverDescriptorMapper.ToDto)];
}

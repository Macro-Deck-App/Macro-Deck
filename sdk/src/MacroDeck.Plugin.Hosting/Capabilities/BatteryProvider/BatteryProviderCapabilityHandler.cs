using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.BatteryProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Hosting.Capabilities.BatteryProvider;

internal sealed class BatteryProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IBatteryProvider> _providers = [.. integrations.OfType<IBatteryProvider>()];

	public string Kind => CapabilityKinds.BatteryProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
	{
		return _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.BatteryProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];
	}

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (string.Equals(invocation.Operation,
			CapabilityOperations.BatteryProvider.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(new BatteryProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name))
					?? metadata.Name
			}));
		}

		return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
			$"The battery-provider capability has no operation '{invocation.Operation}'."));
	}
}

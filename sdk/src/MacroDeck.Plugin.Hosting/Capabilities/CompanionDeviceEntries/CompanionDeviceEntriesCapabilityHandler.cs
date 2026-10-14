using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Hosting.Capabilities.CompanionDeviceEntries;

internal sealed class CompanionDeviceEntriesCapabilityHandler(IEnumerable<IPluginIntegration> integrations)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly bool _declared = integrations.OfType<ICompanionDeviceConfigEntries>().Any();

	public string Kind => CapabilityKinds.CompanionDeviceEntries;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
	{
		return _declared
			?
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.CompanionDeviceEntries, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			]
			: [];
	}

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		return Task.FromResult(string.Equals(invocation.Operation,
			CapabilityOperations.CompanionDeviceEntries.Describe,
			StringComparison.Ordinal)
			? CapabilityInvocationResult.Ok()
			: CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The companion-device-entries capability has no operation '{invocation.Operation}'."));
	}
}

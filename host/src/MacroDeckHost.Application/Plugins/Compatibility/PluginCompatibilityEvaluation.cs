using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeckHost.Application.Plugins.Compatibility;

public sealed record PluginCompatibilityEvaluation
{
	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public int? NegotiatedProtocolVersion { get; init; }

	public IReadOnlyList<CapabilityNegotiationResult> Capabilities { get; init; } = [];

	public PluginSdkUsage? Sdk { get; init; }
}

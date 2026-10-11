namespace MacroDeck.Plugin.Protocol.Capabilities.BatteryProvider;

/// <summary>Result of the <c>describe</c> operation.</summary>
public sealed record BatteryProviderDescribePayload
{
	/// <summary>Human-readable provider name. Empty means the plugin's manifest name is used instead.</summary>
	public string ProviderName { get; init; } = string.Empty;
}

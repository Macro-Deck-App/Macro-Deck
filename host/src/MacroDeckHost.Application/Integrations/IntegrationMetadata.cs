using MacroDeck.Sdk;

namespace MacroDeckHost.Application.Integrations;

public sealed record IntegrationMetadata
{
	public static readonly IntegrationMetadata Default = new();

	public MacroDeckPlatform Platforms { get; init; } = MacroDeckPlatform.All;

	public bool EnabledByDefault { get; init; } = true;
}

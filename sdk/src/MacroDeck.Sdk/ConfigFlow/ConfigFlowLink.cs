using MacroDeck.Localization;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// A labeled link shown on a config flow step, e.g. a link to setup documentation or a
/// provider's developer portal. A step may expose any number of these (or none).
/// </summary>
public sealed class ConfigFlowLink
{
	public required LocalizedText Label { get; init; }

	public required string Url { get; init; }
}

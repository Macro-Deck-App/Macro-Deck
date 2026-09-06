using MacroDeck.Localization;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// A single labeled value the user has to carry over to an external service - a redirect URI to
/// register, an endpoint, a device code to type, a generated token. The UI shows it verbatim with a
/// copy button instead of leaving it interpolated into prose, where it is easy to miss and cannot be
/// copied.
///
/// This is display only and unrelated to <see cref="ConfigFlowValue"/>, which is what a completed flow
/// persists.
/// </summary>
public sealed class ConfigFlowCopyValue
{
	/// <summary>A short caption, e.g. "Redirect URI" - not a sentence.</summary>
	public required LocalizedText Label { get; init; }

	/// <summary>
	/// Rendered monospaced and never reformatted, so pass it exactly as the external service must
	/// receive it.
	/// </summary>
	public required string Value { get; init; }
}

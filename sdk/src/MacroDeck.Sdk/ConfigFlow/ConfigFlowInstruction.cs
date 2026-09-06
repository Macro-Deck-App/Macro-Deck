using MacroDeck.Localization;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// One instruction of a step's setup guide. The UI renders <see cref="ConfigFlowStep.Instructions"/> as
/// an ordered list, so an entry's position IS the number the user sees - never number <see cref="Text"/>
/// yourself.
/// </summary>
public sealed class ConfigFlowInstruction
{
	public required LocalizedText Text { get; init; }

	/// <summary>
	/// Rendered under <see cref="Text"/> as labeled copy blocks. Do not repeat them inside
	/// <see cref="Text"/>; end the sentence with a colon and let the block carry the value.
	/// </summary>
	public IReadOnlyList<ConfigFlowCopyValue> Values { get; init; } = [];
}

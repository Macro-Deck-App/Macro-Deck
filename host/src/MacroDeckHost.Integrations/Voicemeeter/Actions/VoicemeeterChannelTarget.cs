using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed record VoicemeeterChannelTarget(VoicemeeterChannelKind Kind)
{
	public static readonly VoicemeeterChannelTarget Strip = new(VoicemeeterChannelKind.Strip);
	public static readonly VoicemeeterChannelTarget Bus = new(VoicemeeterChannelKind.Bus);

	public string ParameterName => Kind == VoicemeeterChannelKind.Strip
		? VoicemeeterActionValues.StripParameter
		: VoicemeeterActionValues.BusParameter;

	public string Title => Kind == VoicemeeterChannelKind.Strip ? "Strip" : "Bus";

	public string Noun => Kind == VoicemeeterChannelKind.Strip ? "input strip" : "output bus";

	public LocalizedText NounText => Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Common.InputStripNoun()
		: AppStrings.Integrations.Voicemeeter.Common.OutputBusNoun();

	public string IdPrefix => Kind == VoicemeeterChannelKind.Strip ? "strip" : "bus";

	public LocalizedText PickerLabel => Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Common.StripLabel()
		: AppStrings.Integrations.Voicemeeter.Common.BusLabel();

	public LocalizedText PickerDescription => Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Common.WhichStripDescription()
		: AppStrings.Integrations.Voicemeeter.Common.WhichBusDescription();

	public string Parameter(int index, string parameter) => Kind == VoicemeeterChannelKind.Strip
		? VoicemeeterParameters.Strip(index, parameter)
		: VoicemeeterParameters.Bus(index, parameter);

	public ActionParameter Picker() => ActionParameter.DynamicChoice(ParameterName,
		label: PickerLabel,
		description: PickerDescription,
		required: true);

	public VoicemeeterChannel? Channel(VoicemeeterState state, int index) => state.Channel(Kind, index);
}

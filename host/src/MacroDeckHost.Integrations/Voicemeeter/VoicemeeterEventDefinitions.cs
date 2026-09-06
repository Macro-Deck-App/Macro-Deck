using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter;

internal static class VoicemeeterEventDefinitions
{
	private static readonly LocalizedText ConnectionCategory
		= AppStrings.Integrations.Voicemeeter.Events.ConnectionCategory();

	private static readonly LocalizedText StripCategory = AppStrings.Integrations.Voicemeeter.Events.StripsCategory();
	private static readonly LocalizedText BusCategory = AppStrings.Integrations.Voicemeeter.Events.BusesCategory();

	private static readonly LocalizedText MacroCategory
		= AppStrings.Integrations.Voicemeeter.Events.MacroButtonsCategory();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = VoicemeeterEventIds.Connected,
			Name = MacroDeckStrings.Connection.Connected(),
			Description = AppStrings.Integrations.Voicemeeter.Events.ConnectedDescription(),
			Category = ConnectionCategory,
			PayloadParameters =
			[
				ActionParameter.Text("edition",
					label: AppStrings.Integrations.Voicemeeter.Events.Connected.EditionLabel()),
				ActionParameter.Text("version",
					label: AppStrings.Integrations.Voicemeeter.Events.Connected.VersionLabel())
			]
		},
		new()
		{
			Id = VoicemeeterEventIds.Disconnected,
			Name = MacroDeckStrings.Connection.Disconnected(),
			Description = AppStrings.Integrations.Voicemeeter.Events.DisconnectedDescription(),
			Category = ConnectionCategory
		},
		MuteEvent(VoicemeeterEventIds.StripMuteChanged,
			AppStrings.Integrations.Voicemeeter.Events.StripMuteChangedName(),
			AppStrings.Integrations.Voicemeeter.Events.StripMuteDescription(),
			VoicemeeterChannelKind.Strip),
		MuteEvent(VoicemeeterEventIds.BusMuteChanged,
			AppStrings.Integrations.Voicemeeter.Events.BusMuteChangedName(),
			AppStrings.Integrations.Voicemeeter.Events.BusMuteDescription(),
			VoicemeeterChannelKind.Bus),
		GainEvent(VoicemeeterEventIds.StripGainChanged,
			AppStrings.Integrations.Voicemeeter.Events.StripGainChangedName(),
			AppStrings.Integrations.Voicemeeter.Events.StripGainDescription(),
			VoicemeeterChannelKind.Strip),
		GainEvent(VoicemeeterEventIds.BusGainChanged,
			AppStrings.Integrations.Voicemeeter.Events.BusGainChangedName(),
			AppStrings.Integrations.Voicemeeter.Events.BusGainDescription(),
			VoicemeeterChannelKind.Bus),
		new()
		{
			Id = VoicemeeterEventIds.StripRoutingChanged,
			Name = AppStrings.Integrations.Voicemeeter.Events.StripRoutingChangedName(),
			Description = AppStrings.Integrations.Voicemeeter.Events.StripRoutingChangedDescription(),
			Category = StripCategory,
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("strip",
					label: AppStrings.Integrations.Voicemeeter.Common.StripLabel(),
					placeholder: AppStrings.Integrations.Voicemeeter.Events.Common.AnyStrip()),
				ActionParameter.DynamicChoice("bus",
					label: AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusLabel(),
					placeholder: AppStrings.Integrations.Voicemeeter.Events.Common.AnyBus())
			],
			PayloadParameters =
			[
				ActionParameter.Number("strip",
					label: AppStrings.Integrations.Voicemeeter.Events.Common.StripIndexLabel()),
				ActionParameter.Text("name", label: AppStrings.Integrations.Voicemeeter.Common.StripLabel()),
				ActionParameter.DynamicChoice("bus",
					label: AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusLabel()),
				ActionParameter.Toggle("enabled",
					label: AppStrings.Integrations.Voicemeeter.Events.StripRoutingChanged.EnabledLabel())
			]
		},
		new()
		{
			Id = VoicemeeterEventIds.MacroButtonChanged,
			Name = AppStrings.Integrations.Voicemeeter.Events.MacroButtonChangedName(),
			Description = AppStrings.Integrations.Voicemeeter.Events.MacroButtonChangedDescription(),
			Category = MacroCategory,
			ConfigurationParameters =
			[
				ActionParameter.Number("button",
					label: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonLabel(),
					description: AppStrings.Integrations.Voicemeeter.Events.MacroButtonChanged.ButtonDescription(),
					min: 0,
					max: VoicemeeterConnection.MacroButtonCount - 1,
					step: 1)
			],
			PayloadParameters =
			[
				ActionParameter.Number("button",
					label: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonLabel()),
				ActionParameter.Toggle("state", label: AppStrings.Integrations.Voicemeeter.Common.OnOption())
			]
		}
	];

	private static EventDefinition MuteEvent(string id,
		LocalizedText name,
		LocalizedText description,
		VoicemeeterChannelKind kind)
	{
		var channel = ChannelParameterName(kind);

		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = kind == VoicemeeterChannelKind.Strip ? StripCategory : BusCategory,
			ConfigurationParameters = [ChannelFilter(kind)],
			PayloadParameters =
			[
				ActionParameter.Number(channel, label: IndexLabel(kind)),
				ActionParameter.Text("name", label: TitleLabel(kind)),
				ActionParameter.Toggle("muted", label: AppStrings.Integrations.Voicemeeter.Events.Common.MutedLabel())
			]
		};
	}

	private static EventDefinition GainEvent(string id,
		LocalizedText name,
		LocalizedText description,
		VoicemeeterChannelKind kind)
	{
		var channel = ChannelParameterName(kind);

		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = kind == VoicemeeterChannelKind.Strip ? StripCategory : BusCategory,
			ConfigurationParameters = [ChannelFilter(kind)],
			PayloadParameters =
			[
				ActionParameter.Number(channel, label: IndexLabel(kind)),
				ActionParameter.Text("name", label: TitleLabel(kind)),
				ActionParameter.Number("gain", label: AppStrings.Integrations.Voicemeeter.Common.GainLabel()),
				ActionParameter.Number("previousGain",
					label: AppStrings.Integrations.Voicemeeter.Events.Common.PreviousGainLabel())
			]
		};
	}

	private static ActionParameter ChannelFilter(VoicemeeterChannelKind kind)
		=> ActionParameter.DynamicChoice(ChannelParameterName(kind),
			label: TitleLabel(kind),
			placeholder: kind == VoicemeeterChannelKind.Strip
				? AppStrings.Integrations.Voicemeeter.Events.Common.AnyStrip()
				: AppStrings.Integrations.Voicemeeter.Events.Common.AnyBus());

	internal static string ChannelParameterName(VoicemeeterChannelKind kind)
		=> kind == VoicemeeterChannelKind.Strip ? "strip" : "bus";

	private static LocalizedText TitleLabel(VoicemeeterChannelKind kind)
		=> kind == VoicemeeterChannelKind.Strip
			? AppStrings.Integrations.Voicemeeter.Common.StripLabel()
			: AppStrings.Integrations.Voicemeeter.Common.BusLabel();

	private static LocalizedText IndexLabel(VoicemeeterChannelKind kind)
		=> kind == VoicemeeterChannelKind.Strip
			? AppStrings.Integrations.Voicemeeter.Events.Common.StripIndexLabel()
			: AppStrings.Integrations.Voicemeeter.Events.Common.BusIndexLabel();
}

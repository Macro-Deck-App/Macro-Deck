using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Voicemeeter.Connections;
using MacroDeckHost.Integrations.Voicemeeter.State;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Events;

internal static class VoicemeeterEventDefinitions
{
	private static readonly LocalizedText _connectionCategory
		= AppStrings.Integrations.Voicemeeter.Events.ConnectionCategory();

	private static readonly LocalizedText _stripCategory = AppStrings.Integrations.Voicemeeter.Events.StripsCategory();
	private static readonly LocalizedText _busCategory = AppStrings.Integrations.Voicemeeter.Events.BusesCategory();

	private static readonly LocalizedText _macroCategory
		= AppStrings.Integrations.Voicemeeter.Events.MacroButtonsCategory();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = VoicemeeterEventIds.Connected,
			Name = MacroDeckStrings.Connection.Connected(),
			Description = AppStrings.Integrations.Voicemeeter.Events.ConnectedDescription(),
			Category = _connectionCategory,
			PayloadParameters =
			[
				ActionParameter.Text("edition",
					AppStrings.Integrations.Voicemeeter.Events.Connected.EditionLabel()),
				ActionParameter.Text("version",
					AppStrings.Integrations.Voicemeeter.Events.Connected.VersionLabel())
			]
		},
		new()
		{
			Id = VoicemeeterEventIds.Disconnected,
			Name = MacroDeckStrings.Connection.Disconnected(),
			Description = AppStrings.Integrations.Voicemeeter.Events.DisconnectedDescription(),
			Category = _connectionCategory
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
			Category = _stripCategory,
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("strip",
					AppStrings.Integrations.Voicemeeter.Common.StripLabel(),
					placeholder: AppStrings.Integrations.Voicemeeter.Events.Common.AnyStrip()),
				ActionParameter.DynamicChoice("bus",
					AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusLabel(),
					placeholder: AppStrings.Integrations.Voicemeeter.Events.Common.AnyBus())
			],
			PayloadParameters =
			[
				ActionParameter.Number("strip",
					AppStrings.Integrations.Voicemeeter.Events.Common.StripIndexLabel()),
				ActionParameter.Text("name", AppStrings.Integrations.Voicemeeter.Common.StripLabel()),
				ActionParameter.DynamicChoice("bus",
					AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusLabel()),
				ActionParameter.Toggle("enabled",
					AppStrings.Integrations.Voicemeeter.Events.StripRoutingChanged.EnabledLabel())
			]
		},
		new()
		{
			Id = VoicemeeterEventIds.MacroButtonChanged,
			Name = AppStrings.Integrations.Voicemeeter.Events.MacroButtonChangedName(),
			Description = AppStrings.Integrations.Voicemeeter.Events.MacroButtonChangedDescription(),
			Category = _macroCategory,
			ConfigurationParameters =
			[
				ActionParameter.Number("button",
					AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonLabel(),
					AppStrings.Integrations.Voicemeeter.Events.MacroButtonChanged.ButtonDescription(),
					0,
					VoicemeeterConnection.MacroButtonCount - 1,
					1)
			],
			PayloadParameters =
			[
				ActionParameter.Number("button",
					AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonLabel()),
				ActionParameter.Toggle("state", AppStrings.Integrations.Voicemeeter.Common.OnOption())
			]
		}
	];

	private static EventDefinition MuteEvent(
		string id,
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
			Category = kind == VoicemeeterChannelKind.Strip ? _stripCategory : _busCategory,
			ConfigurationParameters = [ChannelFilter(kind)],
			PayloadParameters =
			[
				ActionParameter.Number(channel, IndexLabel(kind)),
				ActionParameter.Text("name", TitleLabel(kind)),
				ActionParameter.Toggle("muted", AppStrings.Integrations.Voicemeeter.Events.Common.MutedLabel())
			]
		};
	}

	private static EventDefinition GainEvent(
		string id,
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
			Category = kind == VoicemeeterChannelKind.Strip ? _stripCategory : _busCategory,
			ConfigurationParameters = [ChannelFilter(kind)],
			PayloadParameters =
			[
				ActionParameter.Number(channel, IndexLabel(kind)),
				ActionParameter.Text("name", TitleLabel(kind)),
				ActionParameter.Number("gain", AppStrings.Integrations.Voicemeeter.Common.GainLabel()),
				ActionParameter.Number("previousGain",
					AppStrings.Integrations.Voicemeeter.Events.Common.PreviousGainLabel())
			]
		};
	}

	private static ActionParameter ChannelFilter(VoicemeeterChannelKind kind)
	{
		return ActionParameter.DynamicChoice(ChannelParameterName(kind),
			TitleLabel(kind),
			placeholder: kind == VoicemeeterChannelKind.Strip
				? AppStrings.Integrations.Voicemeeter.Events.Common.AnyStrip()
				: AppStrings.Integrations.Voicemeeter.Events.Common.AnyBus());
	}

	internal static string ChannelParameterName(VoicemeeterChannelKind kind)
	{
		return kind == VoicemeeterChannelKind.Strip ? "strip" : "bus";
	}

	private static LocalizedText TitleLabel(VoicemeeterChannelKind kind)
	{
		return kind == VoicemeeterChannelKind.Strip
			? AppStrings.Integrations.Voicemeeter.Common.StripLabel()
			: AppStrings.Integrations.Voicemeeter.Common.BusLabel();
	}

	private static LocalizedText IndexLabel(VoicemeeterChannelKind kind)
	{
		return kind == VoicemeeterChannelKind.Strip
			? AppStrings.Integrations.Voicemeeter.Events.Common.StripIndexLabel()
			: AppStrings.Integrations.Voicemeeter.Events.Common.BusIndexLabel();
	}
}

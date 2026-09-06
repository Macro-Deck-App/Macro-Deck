using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord;

internal static class DiscordEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Simple(DiscordEventIds.Connected,
			AppStrings.Integrations.Discord.Events.ConnectedName(),
			AppStrings.Integrations.Discord.Events.ConnectionCategory(),
			AppStrings.Integrations.Discord.Events.ConnectedDescription()),
		Simple(DiscordEventIds.Disconnected,
			AppStrings.Integrations.Discord.Events.DisconnectedName(),
			AppStrings.Integrations.Discord.Events.ConnectionCategory(),
			AppStrings.Integrations.Discord.Events.DisconnectedDescription()),

		Simple(DiscordEventIds.Muted,
			AppStrings.Integrations.Discord.Events.MutedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.MutedDescription()),
		Simple(DiscordEventIds.Unmuted,
			AppStrings.Integrations.Discord.Events.UnmutedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.UnmutedDescription()),
		Simple(DiscordEventIds.Deafened,
			AppStrings.Integrations.Discord.Events.DeafenedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.DeafenedDescription()),
		Simple(DiscordEventIds.Undeafened,
			AppStrings.Integrations.Discord.Events.UndeafenedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.UndeafenedDescription()),

		Simple(DiscordEventIds.ServerMuted,
			AppStrings.Integrations.Discord.Events.ServerMutedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.ServerMutedDescription()),
		Simple(DiscordEventIds.ServerUnmuted,
			AppStrings.Integrations.Discord.Events.ServerUnmutedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.ServerUnmutedDescription()),
		Simple(DiscordEventIds.ServerDeafened,
			AppStrings.Integrations.Discord.Events.ServerDeafenedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.ServerDeafenedDescription()),
		Simple(DiscordEventIds.ServerUndeafened,
			AppStrings.Integrations.Discord.Events.ServerUndeafenedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.ServerUndeafenedDescription()),

		Simple(DiscordEventIds.MicrophoneSilenced,
			AppStrings.Integrations.Discord.Events.MicrophoneSilencedName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.MicrophoneSilencedDescription()),
		Simple(DiscordEventIds.MicrophoneLive,
			AppStrings.Integrations.Discord.Events.MicrophoneLiveName(),
			AppStrings.Integrations.Discord.Events.VoiceCategory(),
			AppStrings.Integrations.Discord.Events.MicrophoneLiveDescription()),

		new()
		{
			Id = DiscordEventIds.VoiceChannelJoined,
			Name = AppStrings.Integrations.Discord.Events.VoiceChannelJoinedName(),
			Description = AppStrings.Integrations.Discord.Events.VoiceChannelJoinedDescription(),
			Category = AppStrings.Integrations.Discord.Events.VoiceChannelCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("channelName",
					label: AppStrings.Integrations.Discord.Events.ChannelLabel(),
					description: AppStrings.Integrations.Discord.Events.ChannelFilterDescription())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("channelName",
					label: AppStrings.Integrations.Discord.Events.ChannelLabel()),
				ActionParameter.Text("channelId", label: AppStrings.Integrations.Discord.Events.ChannelIdLabel()),
				ActionParameter.Text("guildName", label: AppStrings.Integrations.Discord.Events.ServerLabel()),
				ActionParameter.Text("guildId", label: AppStrings.Integrations.Discord.Events.ServerIdLabel())
			]
		},
		new()
		{
			Id = DiscordEventIds.VoiceChannelLeft,
			Name = AppStrings.Integrations.Discord.Events.VoiceChannelLeftName(),
			Description = AppStrings.Integrations.Discord.Events.VoiceChannelLeftDescription(),
			Category = AppStrings.Integrations.Discord.Events.VoiceChannelCategory(),
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("channelName",
					label: AppStrings.Integrations.Discord.Events.ChannelLabel()),
				ActionParameter.Text("channelId", label: AppStrings.Integrations.Discord.Events.ChannelIdLabel())
			]
		},
		new()
		{
			Id = DiscordEventIds.VoiceConnectionStateChanged,
			Name = AppStrings.Integrations.Discord.Events.VoiceConnectionStateChangedName(),
			Description = AppStrings.Integrations.Discord.Events.VoiceConnectionStateChangedDescription(),
			Category = AppStrings.Integrations.Discord.Events.VoiceChannelCategory(),
			ConfigurationParameters =
			[
				ActionParameter.Text("state",
					label: AppStrings.Integrations.Discord.Events.StateLabel(),
					description: AppStrings.Integrations.Discord.Events.StateFilterDescription())
			],
			PayloadParameters =
			[
				ActionParameter.Text("state", label: AppStrings.Integrations.Discord.Events.StateLabel()),
				ActionParameter.Number("averagePing", label: AppStrings.Integrations.Discord.Events.AveragePingLabel())
			]
		},

		Speaking(DiscordEventIds.SpeakingStarted, AppStrings.Integrations.Discord.Events.SpeakingStartedName()),
		Speaking(DiscordEventIds.SpeakingStopped, AppStrings.Integrations.Discord.Events.SpeakingStoppedName()),

		new()
		{
			Id = DiscordEventIds.NotificationReceived,
			Name = AppStrings.Integrations.Discord.Events.NotificationReceivedName(),
			Description = AppStrings.Integrations.Discord.Events.NotificationReceivedDescription(),
			Category = AppStrings.Integrations.Discord.Events.NotificationsCategory(),
			PayloadParameters =
			[
				ActionParameter.Text("title", label: AppStrings.Integrations.Discord.Events.TitleLabel()),
				ActionParameter.Text("body", label: AppStrings.Integrations.Discord.Events.BodyLabel()),
				ActionParameter.Text("channelId", label: AppStrings.Integrations.Discord.Events.ChannelIdLabel()),
				ActionParameter.Text("iconUrl", label: AppStrings.Integrations.Discord.Events.IconUrlLabel())
			]
		}
	];

	private static EventDefinition Speaking(string id, LocalizedText name) => new()
	{
		Id = id,
		Name = name,
		Category = AppStrings.Integrations.Discord.Events.VoiceActivityCategory(),
		PayloadParameters =
		[
			ActionParameter.Text("userId", label: AppStrings.Integrations.Discord.Events.UserIdLabel()),
			ActionParameter.Toggle("isSelf", label: AppStrings.Integrations.Discord.Events.IsSelfLabel())
		]
	};

	private static EventDefinition Simple(string id,
		LocalizedText name,
		LocalizedText category,
		LocalizedText description) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = category
	};
}

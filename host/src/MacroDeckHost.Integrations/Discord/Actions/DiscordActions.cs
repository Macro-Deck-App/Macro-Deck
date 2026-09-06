using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeckHost.Integrations.Discord.Webhooks;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal static class DiscordActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		Func<DiscordConnection?> resolver,
		IDiscordWebhookClient webhooks) =>
	[
		new VoiceToggleActionDefinition("mute",
			AppStrings.Integrations.Discord.Actions.Mute.Name(),
			AppStrings.Integrations.Discord.Actions.Mute.Description(),
			onLabel: AppStrings.Integrations.Discord.Actions.Mute.OnLabel(),
			offLabel: AppStrings.Integrations.Discord.Actions.Mute.OffLabel(),
			resolver,
			state => state.EffectivelySelfMuted,
			DiscordVoicePatches.Mute,
			ActionStates.Mute),
		new VoiceToggleActionDefinition("deafen",
			AppStrings.Integrations.Discord.Actions.Deafen.Name(),
			AppStrings.Integrations.Discord.Actions.Deafen.Description(),
			onLabel: AppStrings.Integrations.Discord.Actions.Deafen.OnLabel(),
			offLabel: AppStrings.Integrations.Discord.Actions.Deafen.OffLabel(),
			resolver,
			state => state.SelfDeafened,
			(target, _) => DiscordVoicePatches.Deafen(target),
			ActionStates.OnOff),

		new VoiceToggleActionDefinition("noise-suppression",
			AppStrings.Integrations.Discord.Actions.NoiseSuppression.Name(),
			AppStrings.Integrations.Discord.Actions.NoiseSuppression.Description(),
			onLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOnOption(),
			offLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOffOption(),
			resolver,
			state => state.NoiseSuppression,
			(target, _) => new DiscordVoiceSettingsPatch { NoiseSuppression = target },
			ActionStates.OnOff),
		new VoiceToggleActionDefinition("echo-cancellation",
			AppStrings.Integrations.Discord.Actions.EchoCancellation.Name(),
			AppStrings.Integrations.Discord.Actions.EchoCancellation.Description(),
			onLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOnOption(),
			offLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOffOption(),
			resolver,
			state => state.EchoCancellation,
			(target, _) => new DiscordVoiceSettingsPatch { EchoCancellation = target },
			ActionStates.OnOff),
		new VoiceToggleActionDefinition("automatic-gain-control",
			AppStrings.Integrations.Discord.Actions.AutomaticGainControl.Name(),
			AppStrings.Integrations.Discord.Actions.AutomaticGainControl.Description(),
			onLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOnOption(),
			offLabel: AppStrings.Integrations.Discord.Actions.VoiceToggle.TurnOffOption(),
			resolver,
			state => state.AutomaticGainControl,
			(target, _) => new DiscordVoiceSettingsPatch { AutomaticGainControl = target },
			ActionStates.OnOff),

		new SetVoiceModeActionDefinition(resolver),
		new SetVoiceVolumeActionDefinition(resolver, output: false),
		new SetVoiceVolumeActionDefinition(resolver, output: true),

		new SelectChannelActionDefinition(resolver, voice: true),
		new DiscordAction("leave-voice-channel",
			AppStrings.Integrations.Discord.Actions.LeaveVoiceChannel.Name(),
			AppStrings.Integrations.Discord.Actions.LeaveVoiceChannel.Description(),
			resolver,
			(connection, ct) => connection.LeaveVoiceChannelAsync(ct)),
		new SelectChannelActionDefinition(resolver, voice: false),

		new SetRichPresenceActionDefinition(resolver),
		new DiscordAction("clear-rich-presence",
			AppStrings.Integrations.Discord.Actions.ClearRichPresence.Name(),
			AppStrings.Integrations.Discord.Actions.ClearRichPresence.Description(),
			resolver,
			(connection, ct) => connection.SetActivityAsync(activity: null, ct)),

		new ExecuteWebhookActionDefinition(webhooks)
	];
}

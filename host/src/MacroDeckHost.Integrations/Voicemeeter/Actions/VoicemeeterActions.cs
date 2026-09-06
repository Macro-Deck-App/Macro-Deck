using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal static class VoicemeeterActions
{
	private static readonly IReadOnlyList<VoicemeeterCommand> _applicationCommands =
	[
		new("show",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Show(),
			VoicemeeterParameters.Command("Show")),
		new("hide",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Hide(),
			VoicemeeterParameters.Command("Show"),
			Argument: 0f),
		new("restart",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Restart(),
			VoicemeeterParameters.Command("Restart")),
		new("lock",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Lock(),
			VoicemeeterParameters.Command("Lock")),
		new("unlock",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Unlock(),
			VoicemeeterParameters.Command("Lock"),
			Argument: 0f),
		new("eject",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Eject(),
			VoicemeeterParameters.Command("Eject")),
		new("reset",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Reset(),
			VoicemeeterParameters.Command("Reset")),
		new("shutdown",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Shutdown(),
			VoicemeeterParameters.Command("Shutdown"))
	];

	private static readonly IReadOnlyList<VoicemeeterCommand> _recorderCommands =
	[
		new("play",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Play(),
			VoicemeeterParameters.Recorder("play")),
		new("stop",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Stop(),
			VoicemeeterParameters.Recorder("stop")),
		new("pause",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Pause(),
			VoicemeeterParameters.Recorder("pause")),
		new("record",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Record(),
			VoicemeeterParameters.Recorder("record")),
		new("replay",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Replay(),
			VoicemeeterParameters.Recorder("replay")),
		new("ff",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.FastForward(),
			VoicemeeterParameters.Recorder("ff")),
		new("rew",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Rewind(),
			VoicemeeterParameters.Recorder("rew"))
	];

	public static IReadOnlyList<IActionDefinition> Create(
		Func<VoicemeeterConnection?> resolver,
		VoicemeeterVariableAccessor variables) =>
	[
		new SetChannelGainActionDefinition(VoicemeeterChannelTarget.Strip, resolver),
		new SetChannelGainActionDefinition(VoicemeeterChannelTarget.Bus, resolver),
		new SetChannelMuteActionDefinition(VoicemeeterChannelTarget.Strip, resolver),
		new SetChannelMuteActionDefinition(VoicemeeterChannelTarget.Bus, resolver),
		new SetStripRoutingActionDefinition(resolver),
		new SetChannelSwitchActionDefinition(VoicemeeterChannelTarget.Strip, resolver),
		new SetChannelSwitchActionDefinition(VoicemeeterChannelTarget.Bus, resolver),

		new SetMacroButtonActionDefinition(resolver),

		new VoicemeeterCommandActionDefinition("run-command",
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Name(),
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.Description(),
			AppStrings.Integrations.Voicemeeter.Actions.RunCommand.CommandLabel(),
			_applicationCommands,
			resolver),
		new VoicemeeterCommandActionDefinition("control-recorder",
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Name(),
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.Description(),
			AppStrings.Integrations.Voicemeeter.Actions.ControlRecorder.TransportLabel(),
			_recorderCommands,
			resolver),
		new LoadSettingsActionDefinition(resolver),
		new RunVoicemeeterActionDefinition(resolver),

		new SetParameterActionDefinition(resolver),
		new RunScriptActionDefinition(resolver),
		new GetParameterActionDefinition(resolver, variables)
	];
}

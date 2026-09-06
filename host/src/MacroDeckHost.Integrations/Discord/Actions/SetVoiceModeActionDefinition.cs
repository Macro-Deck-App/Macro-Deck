using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class SetVoiceModeActionDefinition : IActionDefinition, IStateProviderActionDefinition
{
	internal const string ModeParameter = "voiceMode";
	internal const string ModeToggle = "toggle";

	private readonly Func<DiscordConnection?> _resolver;

	public SetVoiceModeActionDefinition(Func<DiscordConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-voice-mode";

	public LocalizedText Name => AppStrings.Integrations.Discord.Actions.SetVoiceMode.Name();

	public LocalizedText Description => AppStrings.Integrations.Discord.Actions.SetVoiceMode.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = ModeToggle, Label = AppStrings.Integrations.Discord.Actions.SetVoiceMode.ToggleOption()
				},
				new ActionParameterOption
				{
					Value = DiscordVoiceModes.PushToTalk,
					Label = AppStrings.Integrations.Discord.Actions.SetVoiceMode.PushToTalkOption()
				},
				new ActionParameterOption
				{
					Value = DiscordVoiceModes.VoiceActivity,
					Label = AppStrings.Integrations.Discord.Actions.SetVoiceMode.VoiceActivityOption()
				}
			],
			label: AppStrings.Integrations.Discord.Actions.SetVoiceMode.ModeLabel(),
			defaultValue: ModeToggle)
	];

	private static readonly IReadOnlyList<ActionStateDefinition> _states =
	[
		new("push-to-talk", MacroDeckStrings.States.PushToTalk())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2b6cb0" }
		},
		new("voice-activity", MacroDeckStrings.States.VoiceActivity())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		ActionStates.Unavailable
	];

	public IActionExecutor CreateExecutor() => new Executor(Id, _resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = _resolver()?.State;
		var activeId = state is { IsConnected: true }
			? state.VoiceMode switch
			{
				DiscordVoiceModes.PushToTalk => "push-to-talk",
				DiscordVoiceModes.VoiceActivity => "voice-activity",
				_ => ActionStates.Unavailable.Id
			}
			: ActionStates.Unavailable.Id;

		return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(_states, activeId));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetVoiceModeActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly string _name;
		private readonly Func<DiscordConnection?> _resolver;

		public Executor(string name, Func<DiscordConnection?> resolver)
		{
			_name = name;
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord voice mode action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			var requested = context.Parameters.GetValueOrDefault(ModeParameter) as string ?? ModeToggle;
			var target = requested switch
			{
				DiscordVoiceModes.PushToTalk => DiscordVoiceModes.PushToTalk,
				DiscordVoiceModes.VoiceActivity => DiscordVoiceModes.VoiceActivity,
				_ => Flip(connection.State.VoiceMode)
			};

			var result = await connection
				.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch
						{ Mode = new DiscordVoiceModePatch { Type = target } },
					context.CancellationToken)
				.ConfigureAwait(false);

			return DiscordVoiceActionResults.ToActionResult(result, _name);
		}

		private static string Flip(string? current)
			=> string.Equals(current, DiscordVoiceModes.PushToTalk, StringComparison.Ordinal)
				? DiscordVoiceModes.VoiceActivity
				: DiscordVoiceModes.PushToTalk;
	}
}

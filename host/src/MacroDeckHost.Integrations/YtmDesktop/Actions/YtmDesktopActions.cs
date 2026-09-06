using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;

namespace MacroDeckHost.Integrations.YtmDesktop.Actions;

internal static class YtmDesktopActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=>
		[
			new YtmDesktopActionDefinition(resolver,
				getInstances,
				"rate-track",
				AppStrings.Integrations.YtmDesktop.Actions.RateTrackName(),
				AppStrings.Integrations.YtmDesktop.Actions.RateTrackDescription(),
				[RateModeChoice()],
				(player, values, ct) =>
					player.SetRatingAsync(GetString(values, "mode") ?? "like", ct)),
			new YtmDesktopActionDefinition(resolver,
				getInstances,
				"set-mute",
				AppStrings.Integrations.YtmDesktop.Actions.MuteName(),
				AppStrings.Integrations.YtmDesktop.Actions.MuteDescription(),
				[MuteModeChoice()],
				(player, values, ct) =>
					player.SetMuteAsync(GetString(values, "mode") ?? "toggle", ct)),
			new YtmDesktopActionDefinition(resolver,
				getInstances,
				"play-video",
				AppStrings.Integrations.YtmDesktop.Actions.PlayVideoName(),
				AppStrings.Integrations.YtmDesktop.Actions.PlayVideoDescription(),
				[
					ActionParameter.Text("video",
						label: AppStrings.Integrations.YtmDesktop.Actions.VideoLabel(),
						description: AppStrings.Integrations.YtmDesktop.Actions.VideoDescription(),
						placeholder: "https://music.youtube.com/watch?v=...",
						required: true)
				],
				(player, values, ct) =>
					player.PlayVideoOrUrlAsync(GetString(values, "video") ?? string.Empty, ct))
		];

	private static ActionParameter RateModeChoice()
		=> ActionParameter.Choice("mode",
			options:
			[
				new ActionParameterOption
					{ Value = "like", Label = AppStrings.Integrations.YtmDesktop.Actions.RateLike() },
				new ActionParameterOption
					{ Value = "dislike", Label = AppStrings.Integrations.YtmDesktop.Actions.RateDislike() },
				new ActionParameterOption
					{ Value = "clear", Label = AppStrings.Integrations.YtmDesktop.Actions.RateClear() },
				new ActionParameterOption
					{ Value = "toggle-like", Label = AppStrings.Integrations.YtmDesktop.Actions.RateToggleLike() },
				new ActionParameterOption
				{
					Value = "toggle-dislike", Label = AppStrings.Integrations.YtmDesktop.Actions.RateToggleDislike()
				}
			],
			label: AppStrings.Integrations.YtmDesktop.Actions.ModeLabel(),
			defaultValue: "like");

	private static ActionParameter MuteModeChoice()
		=> ActionParameter.Choice("mode",
			options:
			[
				new ActionParameterOption
					{ Value = "toggle", Label = AppStrings.Integrations.YtmDesktop.Actions.MuteToggle() },
				new ActionParameterOption
					{ Value = "on", Label = AppStrings.Integrations.YtmDesktop.Actions.MuteOn() },
				new ActionParameterOption
					{ Value = "off", Label = AppStrings.Integrations.YtmDesktop.Actions.MuteOff() }
			],
			label: AppStrings.Integrations.YtmDesktop.Actions.ModeLabel(),
			defaultValue: "toggle");

	private static string? GetString(IReadOnlyDictionary<string, object> values, string name)
		=> values.TryGetValue(name, out var value) ? value.ToString() : null;

	private sealed class YtmDesktopActionDefinition : MusicPlayerActionDefinition
	{
		private readonly Func<YtmDesktopMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken,
			Task<ActionResult>> _command;

		public YtmDesktopActionDefinition(
			MusicPlayerResolver resolver,
			Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
			string id,
			LocalizedText name,
			LocalizedText description,
			IReadOnlyList<ActionParameter> extraParameters,
			Func<YtmDesktopMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken, Task<ActionResult>>
				command)
			: base(resolver, getInstances, id, name, description, extraParameters, NoOp)
		{
			_command = command;
		}

		public override IActionExecutor CreateExecutor() => new Executor(this);

		private static Task NoOp(
			IMusicPlayer player,
			IReadOnlyDictionary<string, object> values,
			IActionInteractions? interactions,
			CancellationToken cancellationToken)
			=> Task.CompletedTask;

		private sealed class Executor : IActionExecutor
		{
			private readonly YtmDesktopActionDefinition _owner;

			public Executor(YtmDesktopActionDefinition owner)
			{
				_owner = owner;
			}

			public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				var instanceId = context.Parameters
					.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)
					?.ToString();

				if (_owner.Resolve(instanceId) is not YtmDesktopMusicPlayer player)
				{
					return ActionResult.Failed(ActionErrorCodes.NotConfigured,
						AppStrings.Integrations.YtmDesktop.Errors.NotSetUp());
				}

				return await _owner._command(player, context.Parameters, context.CancellationToken);
			}
		}
	}
}

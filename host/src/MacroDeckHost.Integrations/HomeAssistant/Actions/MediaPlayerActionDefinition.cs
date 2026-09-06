using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class MediaPlayerActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string CommandParameterName = "command";
	internal const string VolumeParameterName = "volumePercent";

	private const string MediaPlayerDomain = "media_player";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public MediaPlayerActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "media-player-control";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.EntityLabel(),
			required: true),
		ActionParameter.Choice(CommandParameterName,
			[
				new ActionParameterOption
				{
					Value = "play", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandPlay()
				},
				new ActionParameterOption
				{
					Value = "pause", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandPause()
				},
				new ActionParameterOption
				{
					Value = "play_pause",
					Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandPlayPause()
				},
				new ActionParameterOption
				{
					Value = "stop", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandStop()
				},
				new ActionParameterOption
				{
					Value = "next", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandNext()
				},
				new ActionParameterOption
				{
					Value = "previous",
					Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandPrevious()
				},
				new ActionParameterOption
				{
					Value = "volume_set",
					Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandSetVolume()
				},
				new ActionParameterOption
				{
					Value = "volume_up",
					Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandVolumeUp()
				},
				new ActionParameterOption
				{
					Value = "volume_down",
					Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandVolumeDown()
				},
				new ActionParameterOption
				{
					Value = "mute", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandMute()
				},
				new ActionParameterOption
				{
					Value = "unmute", Label = AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandUnmute()
				}
			],
			label: AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.CommandLabel(),
			defaultValue: "play_pause",
			required: true),
		ActionParameter.Number(VolumeParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.MediaPlayer.VolumeLabel(),
				min: 0,
				max: 100)
			.OnlyWhen(CommandParameterName, "volume_set")
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, MediaPlayerDomain));

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<HomeAssistantConnection?> _resolver;

		public Executor(Func<HomeAssistantConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HomeAssistantServiceCall.TryConnect(_resolver, out var connection, out var rejection))
			{
				return rejection;
			}

			if (!HomeAssistantServiceCall.TryEntity(connection,
				HomeAssistantActionValues.ReadText(context.Parameters, EntityParameterName),
				out var entityId,
				out var rejected))
			{
				return rejected;
			}

			var command = HomeAssistantActionValues.ReadText(context.Parameters, CommandParameterName) ?? "play_pause";
			var data = new Dictionary<string, object?>(StringComparer.Ordinal);

			string service;
			switch (command)
			{
				case "play":
					service = "media_play";
					break;
				case "pause":
					service = "media_pause";
					break;
				case "stop":
					service = "media_stop";
					break;
				case "next":
					service = "media_next_track";
					break;
				case "previous":
					service = "media_previous_track";
					break;
				case "volume_set":
					service = "volume_set";
					data["volume_level"] =
						(HomeAssistantActionValues.ReadNumber(context.Parameters, VolumeParameterName, 0, 100) ?? 0d) /
						100d;
					break;
				case "volume_up":
					service = "volume_up";
					break;
				case "volume_down":
					service = "volume_down";
					break;
				case "mute":
					service = "volume_mute";
					data["is_volume_muted"] = true;
					break;
				case "unmute":
					service = "volume_mute";
					data["is_volume_muted"] = false;
					break;
				default:
					service = "media_play_pause";
					break;
			}

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				MediaPlayerDomain,
				service,
				HomeAssistantServiceCall.Target(entityId),
				data.Count > 0 ? data : null,
				context.CancellationToken);
		}
	}
}

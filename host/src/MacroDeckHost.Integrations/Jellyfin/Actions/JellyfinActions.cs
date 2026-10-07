using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Actions;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Params;

namespace MacroDeckHost.Integrations.Jellyfin.Actions;

internal delegate JellyfinMusicPlayer? JellyfinPlayerResolver(string? instanceId);

internal static class JellyfinActions
{
	public const string ItemParameter = "item";

	private static readonly IReadOnlyList<string> _audioTypes = ["Audio", "MusicAlbum"];
	private static readonly IReadOnlyList<string> _videoTypes = ["Movie", "Episode", "MusicVideo"];

	public static IReadOnlyList<IActionDefinition> Create(
		JellyfinPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=>
		[
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"stop",
				Strings.StopName(),
				Strings.StopDescription(),
				[],
				(player, _, ct) => player.StopAsync(ct)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"seek-forward",
				Strings.SeekForwardName(),
				Strings.SeekForwardDescription(),
				[SecondsParameter(30)],
				(player, values, ct) => player.SeekRelativeAsync(TimeSpan.FromSeconds(Seconds(values, 30)), ct)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"seek-backward",
				Strings.SeekBackwardName(),
				Strings.SeekBackwardDescription(),
				[SecondsParameter(10)],
				(player, values, ct) => player.SeekRelativeAsync(TimeSpan.FromSeconds(-Seconds(values, 10)), ct)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"mute",
				Strings.MuteName(),
				Strings.MuteDescription(),
				[],
				(player, _, ct) => player.GeneralCommandAsync("Mute", null, ct)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"unmute",
				Strings.UnmuteName(),
				Strings.UnmuteDescription(),
				[],
				(player, _, ct) => player.GeneralCommandAsync("Unmute", null, ct)),
			new JellyfinStatePlayerActionDefinition(resolver,
				getInstances,
				"toggle-mute",
				Strings.ToggleMuteName(),
				Strings.ToggleMuteDescription(),
				(player, _, ct) => player.GeneralCommandAsync("ToggleMute", null, ct),
				player => ActionStates.Snapshot(ActionStates.Mute, player?.CurrentSession()?.IsMuted)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"display-message",
				Strings.DisplayMessageName(),
				Strings.DisplayMessageDescription(),
				[
					ActionParameter.Text("header", label: Params.MessageHeader()),
					ActionParameter.Text("text", label: Params.MessageText(), required: true),
					ActionParameter.Number("timeoutSeconds",
						label: Params.MessageTimeout(),
						min: 1,
						max: 600,
						defaultValue: 5)
				],
				(player, values, ct) => player.DisplayMessageAsync(Text(values, "header"),
					Text(values, "text") ?? string.Empty,
					(int)(Seconds(values, 5, "timeoutSeconds") * 1000),
					ct)),
			new JellyfinPlayerActionDefinition(resolver,
				getInstances,
				"play-media",
				Strings.PlayMediaName(),
				Strings.PlayMediaDescription(),
				[ActionParameter.Autocomplete(ItemParameter, label: Params.Item(), required: true)],
				(player, values, ct) =>
				{
					var (mediaType, itemId) = SplitItem(Text(values, ItemParameter));
					return itemId.Length == 0
						? throw new JellyfinCommandException(ActionErrorCodes.InvalidParameter, Strings.ChooseItem())
						: player.PlayItemAsync(itemId, mediaType, ct);
				},
				searchItems: SearchItemsAsync)
		];

	internal static string ItemValue(string mediaType, string itemId) => $"{mediaType}|{itemId}";

	internal static (string? MediaType, string ItemId) SplitItem(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return (null, string.Empty);
		}

		var separator = value.IndexOf('|', StringComparison.Ordinal);
		return separator < 0 ? (null, value) : (value[..separator], value[(separator + 1)..]);
	}

	private static async Task<DynamicOptionsResult> SearchItemsAsync(
		JellyfinMusicPlayer? player,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (player is null || string.IsNullOrWhiteSpace(filter) || !player.Runtime.Connection.State.IsConnected)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var session = player.CurrentSession();
		var types = new List<string>();
		if (session is null || session.PlayableMediaTypes.Count == 0 || session.PlayableMediaTypes.Contains("Audio"))
		{
			types.AddRange(_audioTypes);
		}

		if (session is null || session.PlayableMediaTypes.Count == 0 || session.PlayableMediaTypes.Contains("Video"))
		{
			types.AddRange(_videoTypes);
		}

		var items = await player.Runtime.Connection.Client
			.SearchItemsAsync(filter.Trim(), types, session?.UserId, cancellationToken)
			.ConfigureAwait(false);

		return new DynamicOptionsResult
		{
			Options =
			[
				.. items.Where(item => !string.IsNullOrEmpty(item.Id))
					.Select(item => new ActionParameterOption
					{
						Value = ItemValue(_audioTypes.Contains(item.Type ?? string.Empty) ? "Audio" : "Video", item.Id!),
						Label = Label(item)
					})
			],
			CacheSeconds = 10
		};
	}

	private static string Label(Protocol.JellyfinItemDto item)
	{
		var context = item.SeriesName ?? item.AlbumArtist ?? item.ProductionYear?.ToString(CultureInfo.InvariantCulture);
		return string.IsNullOrEmpty(context) ? item.Name ?? string.Empty : $"{item.Name} · {context}";
	}

	private static ActionParameter SecondsParameter(double defaultValue)
		=> ActionParameter.Number("seconds", label: Params.Seconds(), min: 1, max: 3600, defaultValue: defaultValue);

	private static double Seconds(IReadOnlyDictionary<string, object> values, double fallback, string name = "seconds")
	{
		var value = values.GetValueOrDefault(name);
		var parsed = value switch
		{
			double d => d,
			long l => l,
			int i => i,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
			_ => fallback
		};
		return parsed > 0 ? parsed : fallback;
	}

	private static string? Text(IReadOnlyDictionary<string, object> values, string name)
		=> values.GetValueOrDefault(name)?.ToString() is { Length: > 0 } text ? text : null;
}

internal class JellyfinPlayerActionDefinition : IDynamicOptionsActionDefinition
{
	private static readonly ILogger _logger = IntegrationLog.For<JellyfinPlayerActionDefinition>(JellyfinIntegration.IntegrationId);

	private readonly JellyfinPlayerResolver _resolver;
	private readonly Func<IReadOnlyList<MusicPlayerInstance>> _getInstances;
	private readonly Func<JellyfinMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken, Task> _command;
	private readonly Func<JellyfinMusicPlayer?, string?, CancellationToken, Task<DynamicOptionsResult>>? _searchItems;

	public JellyfinPlayerActionDefinition(
		JellyfinPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<JellyfinMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken, Task> command,
		Func<JellyfinMusicPlayer?, string?, CancellationToken, Task<DynamicOptionsResult>>? searchItems = null)
	{
		_resolver = resolver;
		_getInstances = getInstances;
		_command = command;
		_searchItems = searchItems;
		Id = id;
		Name = name;
		Description = description;
		Parameters =
		[
			ActionParameter.DynamicChoice(MusicPlayerActions.InstanceParameterName,
				label: Params.Player(),
				description: Params.PlayerDescription(),
				placeholder: Params.ActiveSession()),
			.. parameters
		];
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(this);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == MusicPlayerActions.InstanceParameterName)
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					.. _getInstances().Select(instance => new ActionParameterOption
						{ Value = instance.Id, Label = instance.DisplayName })
				],
				CacheSeconds = 2
			});
		}

		if (context.ParameterName == JellyfinActions.ItemParameter && _searchItems is not null)
		{
			var instance = context.CurrentParameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			return _searchItems(_resolver(instance), context.Filter, cancellationToken);
		}

		return Task.FromResult(new DynamicOptionsResult { Options = [] });
	}

	protected JellyfinMusicPlayer? Resolve(IReadOnlyDictionary<string, object?> parameters)
		=> _resolver(parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString());

	private sealed class Executor(JellyfinPlayerActionDefinition definition) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var instance = context.Parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			var player = definition._resolver(instance);
			if (player is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, Strings.NoServer());
			}

			try
			{
				await definition._command(player, context.Parameters, context.CancellationToken).ConfigureAwait(false);
				return ActionResult.Success();
			}
			catch (JellyfinCommandException ex)
			{
				return ActionResult.Failed(ex.Code, ex.LocalizedMessage);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Jellyfin action {Action} failed", definition.Id);
				return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Failed());
			}
		}
	}
}

internal sealed class JellyfinStatePlayerActionDefinition(
	JellyfinPlayerResolver resolver,
	Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
	string id,
	LocalizedText name,
	LocalizedText description,
	Func<JellyfinMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken, Task> command,
	Func<JellyfinMusicPlayer?, ActionStateSnapshot> state)
	: JellyfinPlayerActionDefinition(resolver, getInstances, id, name, description, [], command),
		IStateProviderActionDefinition
{
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionStateSnapshot?>(state(Resolve(parameters)));
}

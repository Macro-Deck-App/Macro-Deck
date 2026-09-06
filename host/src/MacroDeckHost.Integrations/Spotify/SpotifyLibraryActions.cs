using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyLibraryActions
{
	public static IReadOnlyList<IActionDefinition> All(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=>
		[
			ToggleLiked(resolver, getInstances),
			new SpotifyPlaylistMembershipActionDefinition(resolver, getInstances)
		];

	private static SpotifyLikedActionDefinition ToggleLiked(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=> new(resolver, getInstances);

	// The SDK hands every music-player command an IMusicPlayer, not a SpotifyMusicPlayer, so the
	// SinusBot no-op here is deliberate rather than accidental.
	private static Task ToggleLikedCommand(
		IMusicPlayer player,
		IReadOnlyDictionary<string, object> values,
		IActionInteractions? _,
		CancellationToken cancellationToken)
		=> player is SpotifyMusicPlayer spotify
			? spotify.ToggleLikedAsync(GetString(values, "mode") ?? "toggle", cancellationToken)
			: Task.CompletedTask;

	private static ActionParameter ModeChoice(params string[] values)
		=> ActionParameter.Choice("mode",
			options: values.Select(v => new ActionParameterOption { Value = v, Label = ModeLabel(v) }).ToList(),
			label: AppStrings.Integrations.Spotify.Actions.ModeLabel(),
			defaultValue: values[0]);

	private static LocalizedText ModeLabel(string value) => value switch
	{
		"toggle" => AppStrings.Integrations.Spotify.Actions.ModeToggle(),
		"add" => AppStrings.Integrations.Spotify.Actions.ModeAdd(),
		"remove" => AppStrings.Integrations.Spotify.Actions.ModeRemove(),
		_ => value
	};

	private static string? GetString(IReadOnlyDictionary<string, object> values, string name)
		=> values.TryGetValue(name, out var value) ? value.ToString() : null;

	private static readonly ActionStateDefinition _unavailableState =
		new("unavailable", MacroDeckStrings.States.Unavailable())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" }
		};

	private sealed class SpotifyLikedActionDefinition : MusicPlayerActionDefinition, IStateProviderActionDefinition
	{
		private static readonly IReadOnlyList<ActionStateDefinition> _states =
		[
			new("not-liked", MacroDeckStrings.States.NotLiked())
			{
				DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568" }
			},
			new("liked", MacroDeckStrings.States.Liked())
			{
				DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#1db954" }
			},
			_unavailableState
		];

		public SpotifyLikedActionDefinition(
			MusicPlayerResolver resolver,
			Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
			: base(resolver,
				getInstances,
				"toggle-liked",
				AppStrings.Integrations.Spotify.Actions.ToggleLikedName(),
				AppStrings.Integrations.Spotify.Actions.ToggleLikedDescription(),
				[ModeChoice("toggle", "add", "remove")],
				ToggleLikedCommand)
		{
		}

		// Answers from SpotifyMusicPlayer's own saved-state cache, which owns the TTL, the failure
		// backoff and the 403 latch-off, so a button following this costs no request of its own beyond
		// the one the cache already refreshes.
		public async Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			if (Resolve(parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString())
				is not SpotifyMusicPlayer spotify)
			{
				return null;
			}

			var liked = await spotify.IsCurrentItemSavedAsync(cancellationToken);
			return new ActionStateSnapshot(_states,
				liked switch
				{
					true => "liked",
					false => "not-liked",
					null => _unavailableState.Id
				});
		}
	}

	private sealed class SpotifyPlaylistMembershipActionDefinition
		: MusicPlayerActionDefinition, IStateProviderActionDefinition
	{
		private static readonly IReadOnlyList<ActionStateDefinition> _states =
		[
			new("not-in-playlist", MacroDeckStrings.States.NotInPlaylist())
			{
				DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568" }
			},
			new("in-playlist", MacroDeckStrings.States.InPlaylist())
			{
				DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#1db954" }
			},
			_unavailableState
		];

		private const string PlaylistParameterName = "playlist";

		private static readonly ILogger _logger =
			IntegrationLog.For<SpotifyPlaylistMembershipActionDefinition>(SpotifyIntegration.IntegrationId);

		private readonly MusicPlayerResolver _resolver;
		private readonly Func<IReadOnlyList<MusicPlayerInstance>> _getInstances;

		public SpotifyPlaylistMembershipActionDefinition(
			MusicPlayerResolver resolver,
			Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
			: base(resolver,
				getInstances,
				"playlist-membership",
				AppStrings.Integrations.Spotify.Actions.PlaylistMembershipName(),
				AppStrings.Integrations.Spotify.Actions.PlaylistMembershipDescription(),
				[
					ActionParameter.Autocomplete(PlaylistParameterName,
						label: AppStrings.Integrations.Spotify.Actions.PlaylistLabel(),
						description: AppStrings.Integrations.Spotify.Actions.PlaylistDescription(),
						placeholder: AppStrings.Integrations.Spotify.Actions.PlaylistPlaceholder(),
						required: true),
					ModeChoice("add", "remove")
				],
				Execute)
		{
			_resolver = resolver;
			_getInstances = getInstances;
		}

		private static Task Execute(
			IMusicPlayer player,
			IReadOnlyDictionary<string, object> values,
			IActionInteractions? _,
			CancellationToken cancellationToken)
		{
			if (player is not SpotifyMusicPlayer spotify)
			{
				return Task.CompletedTask;
			}

			var playlist = GetString(values, PlaylistParameterName);
			if (string.IsNullOrWhiteSpace(playlist))
			{
				return Task.CompletedTask;
			}

			return spotify.ChangePlaylistMembershipAsync(playlist.Trim(),
				GetString(values, "mode") ?? "add",
				cancellationToken);
		}

		// No instance, nothing playing, or no complete playlist value yet is "nothing to say" rather than
		// an unavailable state: the action is still being configured, and claiming a state set there would
		// have the editor adopt states for a target that is not chosen yet.
		public async Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			if (Resolve(parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString())
				is not SpotifyMusicPlayer spotify)
			{
				return null;
			}

			if (parameters.GetValueOrDefault(PlaylistParameterName)?.ToString() is not { } playlist ||
				string.IsNullOrWhiteSpace(playlist))
			{
				return null;
			}

			var inPlaylist = await spotify.IsCurrentItemInPlaylistAsync(playlist.Trim(), cancellationToken);
			return new ActionStateSnapshot(_states,
				inPlaylist switch
				{
					true => "in-playlist",
					false => "not-in-playlist",
					null => _unavailableState.Id
				});
		}

		public override async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
			DynamicOptionsContext context,
			CancellationToken cancellationToken)
		{
			if (context.ParameterName == MusicPlayerActions.InstanceParameterName)
			{
				return new DynamicOptionsResult { Options = InstanceOptions(_getInstances()), CacheSeconds = 2 };
			}

			if (context.ParameterName != PlaylistParameterName)
			{
				return new DynamicOptionsResult { Options = [] };
			}

			var instanceId = ResolveInstanceId(context.CurrentParameters);
			if (instanceId is null || _resolver(instanceId) is not IMusicPlayerCatalogProvider catalog)
			{
				return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
			}

			try
			{
				var items = await catalog.GetCatalogAsync(instanceId,
					MusicPlayerCatalogItemKind.Playlist,
					context.Filter,
					cancellationToken);
				var options = items.Select(i => new ActionParameterOption { Value = i.Id, Label = i.Title }).ToList();
				return new DynamicOptionsResult { Options = options, AllowsCustomValue = true, CacheSeconds = 5 };
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
			}
			catch (Exception ex)
			{
				_logger.Warning("Could not load playlist suggestions for {Instance} ({Failure})",
					instanceId,
					ex.GetType().Name);
				return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
			}
		}

		private string? ResolveInstanceId(IReadOnlyDictionary<string, object?> currentParameters)
		{
			var instanceId = currentParameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			if (!string.IsNullOrEmpty(instanceId))
			{
				return instanceId;
			}

			var instances = _getInstances();
			return instances.Count > 0 ? instances[0].Id : null;
		}
	}
}

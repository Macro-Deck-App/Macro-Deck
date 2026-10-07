using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.SoundPad;

internal static class SoundPadActions
{
	public const string PlaySoundId = "play-sound";
	public const string StopPlaybackId = "stop-playback";
	public const string StartRecordingId = "start-recording";
	public const string StopRecordingId = "stop-recording";
	public const string PlayRandomSoundId = "play-random-sound";
	public const string ToggleMuteId = "toggle-mute";

	public const string SoundParameter = "track";
	public const string SourceParameter = "source";
	public const string CategoryParameter = "category";
	public const string SpeakersParameter = "speakers";
	public const string MicrophoneParameter = "microphone";

	public const string SourceDefault = "default";
	public const string SourceMicrophone = "microphone";
	public const string SourceSpeakers = "speakers";

	private static readonly HashSet<string> _unsupportedCommon = new(StringComparer.Ordinal)
	{
		"toggle-shuffle", "set-repeat-mode"
	};

	public static IReadOnlyList<IActionDefinition> Create(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=>
		[
			new MusicPlayerItemActionDefinition(integrationId,
				resolver,
				getInstances,
				PlaySoundId,
				AppStrings.Integrations.SoundPad.Actions.PlaySoundName(),
				AppStrings.Integrations.SoundPad.Actions.PlaySoundDescription(),
				MusicPlayerCatalogItemKind.Track,
				SoundParameter,
				"Sound"),
			Command(resolver,
				getInstances,
				StopPlaybackId,
				AppStrings.Integrations.SoundPad.Actions.StopPlaybackName(),
				AppStrings.Integrations.SoundPad.Actions.StopPlaybackDescription(),
				[],
				(player, _, ct) => player.StopAsync(ct)),
			Command(resolver,
				getInstances,
				StartRecordingId,
				AppStrings.Integrations.SoundPad.Actions.StartRecordingName(),
				AppStrings.Integrations.SoundPad.Actions.StartRecordingDescription(),
				[
					ActionParameter.Choice(SourceParameter,
						options:
						[
							new ActionParameterOption
							{
								Value = SourceDefault,
								Label = AppStrings.Integrations.SoundPad.Actions.SourceDefault()
							},
							new ActionParameterOption
							{
								Value = SourceMicrophone,
								Label = AppStrings.Integrations.SoundPad.Actions.SourceMicrophone()
							},
							new ActionParameterOption
							{
								Value = SourceSpeakers,
								Label = AppStrings.Integrations.SoundPad.Actions.SourceSpeakers()
							}
						],
						label: AppStrings.Integrations.SoundPad.Actions.SourceLabel(),
						defaultValue: SourceDefault,
						required: true)
				],
				(player, values, ct) => player.StartRecordingAsync(ParseSource(ReadText(values, SourceParameter)), ct)),
			Command(resolver,
				getInstances,
				StopRecordingId,
				AppStrings.Integrations.SoundPad.Actions.StopRecordingName(),
				AppStrings.Integrations.SoundPad.Actions.StopRecordingDescription(),
				[],
				(player, _, ct) => player.StopRecordingAsync(ct)),
			new PlayRandomSoundActionDefinition(resolver, getInstances),
			Command(resolver,
				getInstances,
				ToggleMuteId,
				AppStrings.Integrations.SoundPad.Actions.ToggleMuteName(),
				AppStrings.Integrations.SoundPad.Actions.ToggleMuteDescription(),
				[],
				(player, _, ct) => player.ToggleMuteAsync(ct)),
			.. MusicPlayerActions.Common(resolver, getInstances).Where(action => !_unsupportedCommon.Contains(action.Id))
		];

	public static SoundPadRecordingSource ParseSource(string? value) => value switch
	{
		SourceMicrophone => SoundPadRecordingSource.Microphone,
		SourceSpeakers => SoundPadRecordingSource.Speakers,
		_ => SoundPadRecordingSource.Default
	};

	private static MusicPlayerActionDefinition Command(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<SoundPadMusicPlayer, IReadOnlyDictionary<string, object>, CancellationToken, Task> command)
		=> new(resolver,
			getInstances,
			id,
			name,
			description,
			parameters,
			(player, values, _, ct) => command(AsSoundPad(player), values, ct));

	private static SoundPadMusicPlayer AsSoundPad(IMusicPlayer player)
		=> player as SoundPadMusicPlayer ??
			throw new InvalidOperationException("The resolved music player is not SoundPad.");

	private static string? ReadText(IReadOnlyDictionary<string, object> values, string name)
	{
		var text = values.GetValueOrDefault(name) switch
		{
			null => null,
			string s => s,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			var other => other.ToString()
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	private static bool ReadBool(IReadOnlyDictionary<string, object> values, string name, bool fallback)
		=> values.GetValueOrDefault(name) switch
		{
			bool b => b,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => fallback
		};

	private sealed class PlayRandomSoundActionDefinition : MusicPlayerActionDefinition
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<PlayRandomSoundActionDefinition>(SoundPadIntegration.IntegrationId);

		private readonly MusicPlayerResolver _resolver;

		public PlayRandomSoundActionDefinition(
			MusicPlayerResolver resolver,
			Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
			: base(resolver,
				getInstances,
				PlayRandomSoundId,
				AppStrings.Integrations.SoundPad.Actions.PlayRandomSoundName(),
				AppStrings.Integrations.SoundPad.Actions.PlayRandomSoundDescription(),
				[
					ActionParameter.DynamicChoice(CategoryParameter,
						label: AppStrings.Integrations.SoundPad.Actions.CategoryLabel(),
						description: AppStrings.Integrations.SoundPad.Actions.CategoryDescription(),
						placeholder: AppStrings.Integrations.SoundPad.Actions.AllSounds()),
					ActionParameter.Toggle(SpeakersParameter,
						label: AppStrings.Integrations.SoundPad.Actions.PlayOnSpeakersLabel(),
						defaultValue: true),
					ActionParameter.Toggle(MicrophoneParameter,
						label: AppStrings.Integrations.SoundPad.Actions.PlayOnMicrophoneLabel(),
						defaultValue: true)
				],
				(player, values, _, ct) => AsSoundPad(player)
					.PlayRandomSoundAsync(ReadCategory(values),
						ReadBool(values, SpeakersParameter, true),
						ReadBool(values, MicrophoneParameter, true),
						ct))
		{
			_resolver = resolver;
		}

		public override async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
			CancellationToken cancellationToken)
		{
			if (context.ParameterName != CategoryParameter)
			{
				return await base.GetDynamicOptionsAsync(context, cancellationToken);
			}

			var instanceId = context.CurrentParameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)
				?.ToString();
			if (_resolver(string.IsNullOrEmpty(instanceId) ? null : instanceId) is not SoundPadMusicPlayer player)
			{
				return new DynamicOptionsResult { Options = [] };
			}

			IReadOnlyList<SoundPadCategory> categories;
			try
			{
				categories = await player.GetCategoriesAsync(cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Could not load the SoundPad categories");
				return new DynamicOptionsResult { Options = [] };
			}

			List<ActionParameterOption> options =
			[
				new() { Value = "", Label = AppStrings.Integrations.SoundPad.Actions.AllSounds() },
				.. categories.Select(category => new ActionParameterOption
				{
					Value = category.Index.ToString(CultureInfo.InvariantCulture), Label = category.Name
				})
			];

			return new DynamicOptionsResult { Options = options, CacheSeconds = 5 };
		}

		private static int? ReadCategory(IReadOnlyDictionary<string, object> values)
			=> int.TryParse(ReadText(values, CategoryParameter),
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var index)
				? index
				: null;
	}
}

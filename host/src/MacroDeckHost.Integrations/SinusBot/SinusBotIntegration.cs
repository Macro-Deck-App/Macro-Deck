using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.SinusBot;

[MacroDeckIntegration]
public sealed partial class SinusBotIntegration
	: IIntegration, IVariableProvider, IConfigFlowProvider, IMusicPlayerProvider, IIntegrationIconProvider,
		IMigrationProvider
{
	public const string IntegrationId = "app.macro-deck.sinusbot";

	private const string ArtworkBaseUrl = "/api/music-player/artwork/";

	private static readonly ILogger _logger =
		IntegrationLog.For<SinusBotIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		SinusBotVariables.Declare(VariableNameTemplate.Placeholder("instance"));

	private IIntegrationContext? _context;
	private volatile List<Instance> _instances = [];

	public string Id => IntegrationId;
	public LocalizedText Name => "SinusBot";
	public string Version => "1.0.0";
	public bool IsInitialized { get; private set; }

	private static readonly string[] _variableNames =
	[
		"current_track_name",
		"current_artist",
		"current_album",
		"playback_state",
		"is_playing",
		"volume",
		"track_duration",
		"current_position",
		"progress_percentage",
		"album_art_url",
		"device_name",
		"device_type",
		"shuffle_enabled",
		"repeat_mode",
		"is_connected"
	];

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public SinusBotIntegration()
	{
		Actions =
		[
			.. MusicPlayerActions.Common(ResolvePlayer, GetInstances),
			MusicPlayerActions.PlayTrack(Id, ResolvePlayer, GetInstances)
		];
	}

	public string IconMimeType => "image/svg+xml";

	private SinusBotMusicPlayer? DefaultPlayer => _instances.Count > 0 ? _instances[0].Player : null;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _instances.SelectMany(instance =>
				SinusBotVariables.Declare(instance.Key, new VariableConfiguration(instance.EntryId, instance.Title)))
		];

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> DeclaredVariables
	{
		get
		{
			var provided = Variables;
			return provided.Count > 0 ? provided : _templateVariables;
		}
	}

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new SinusBotConfigFlow();

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new SinusBotMacroDeck2Migration()];

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> _instances.Select(i => new MusicPlayerInstance(i.EntryId, i.Title)).ToList();

	public IMusicPlayer? GetPlayer(string instanceId)
		=> _instances.FirstOrDefault(i => i.EntryId == instanceId)?.Player;

	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> string.IsNullOrEmpty(instanceId) ? DefaultPlayer : GetPlayer(instanceId);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		await ConnectFromConfig(context);
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		foreach (var instance in _instances)
		{
			instance.Player.Disconnect();
		}

		_instances = [];
		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var (instance, variableName) = Resolve(localId);
		var state = instance?.Player.LastState;

		object? value = variableName switch
		{
			"current_track_name" => state?.TrackName,
			"current_artist" => state?.Artists.Count > 0 ? string.Join(", ", state.Artists) : null,
			"current_album" => state?.AlbumName,
			"playback_state" => state?.PlaybackState.ToString().ToLowerInvariant(),
			"is_playing" => state?.PlaybackState == PlaybackState.Playing,
			SinusBotVariables.VolumeName => state?.VolumePercent,
			"track_duration" => state?.Duration is { } d ? (int)d.TotalSeconds : null,
			SinusBotVariables.CurrentPositionName => state?.Position is { } p ? (int)p.TotalSeconds : null,
			"progress_percentage" => Progress(state),
			"album_art_url" => ArtworkUrl(instance),
			"device_name" => state?.DeviceName,
			"device_type" => state?.DeviceType,
			"shuffle_enabled" => state?.ShuffleEnabled,
			"repeat_mode" => state?.RepeatMode.ToString().ToLowerInvariant(),
			"is_connected" => state?.IsConnected,
			_ => null
		};

		return ValueTask.FromResult(variableName switch
		{
			SinusBotVariables.VolumeName => VariableReading.Of(value, 0, 100, 1),
			// The seek range is the current track's length, which is exactly why bounds ride on the
			// reading rather than on the declaration.
			SinusBotVariables.CurrentPositionName => VariableReading.Of(value,
				0,
				state?.Duration is { } length ? (int)length.TotalSeconds : null,
				1),
			_ => VariableReading.Of(value)
		});
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var (instance, variableName) = Resolve(localId);

		// Passing null rather than a disconnected player is what turns "the bot is unreachable" into a
		// retryable Unavailable instead of an outright failure.
		var player = instance?.Player is { LastState.IsConnected: true } connected ? connected : null;

		return variableName switch
		{
			SinusBotVariables.VolumeName =>
				MusicPlayerVariableWrites.SetVolumeAsync(player, value, cancellationToken),
			SinusBotVariables.CurrentPositionName =>
				MusicPlayerVariableWrites.SeekAsync(player, value, cancellationToken),
			_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
		};
	}

	// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can
	// never contain '-', so swapping the separator back recovers the name exactly.
	private (Instance? Instance, string VariableName) Resolve(string localId)
	{
		var name = localId.Replace('-', '_');
		var withoutPrefix = name.StartsWith("sinusbot_", StringComparison.OrdinalIgnoreCase)
			? name["sinusbot_".Length..]
			: name;

		foreach (var candidate in _instances)
		{
			var keyPrefix = $"{candidate.Key}_";
			if (!withoutPrefix.StartsWith(keyPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var remainder = withoutPrefix[keyPrefix.Length..];
			if (_variableNames.Contains(remainder, StringComparer.Ordinal))
			{
				return (candidate, remainder);
			}
		}

		return (null, withoutPrefix);
	}

	private string? ArtworkUrl(Instance? instance)
	{
		var instanceId = instance?.EntryId;
		if (instance?.Player.LastState.ArtworkId is not { } artworkId || instanceId is null)
		{
			return null;
		}

		return
			$"{ArtworkBaseUrl}{Uri.EscapeDataString(artworkId)}?instanceId={Uri.EscapeDataString($"{Id}::{instanceId}")}";
	}

	private static int? Progress(MusicPlayerState? state)
	{
		if (state?.Duration is not { } duration ||
			duration.TotalMilliseconds <= 0 ||
			state.Position is not { } position)
		{
			return null;
		}

		return (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100);
	}

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		foreach (var existing in _instances)
		{
			existing.Player.Disconnect();
		}

		var entries = await context.Config.GetEntriesAsync();
		var instances = new List<Instance>();
		var usedKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var entry in entries)
		{
			var player = await BuildPlayer(context, entry.Id);
			if (player is not null)
			{
				instances.Add(new Instance(entry.Id.ToString(), entry.Title, UniqueKey(entry.Title, usedKeys), player));
			}
		}

		_instances = instances;
		_logger.Information("SinusBot connected with {Count} instance(s)", instances.Count);
	}

	private static async Task<SinusBotMusicPlayer?> BuildPlayer(IIntegrationContext context, Guid entryId)
	{
		var serverUrl = await context.Config.GetStringAsync(entryId, SinusBotConfigKeys.ServerUrl);
		var username = await context.Config.GetStringAsync(entryId, SinusBotConfigKeys.Username);
		var password = await context.Config.GetSecretAsync(entryId, SinusBotConfigKeys.Password);
		var instanceId = await context.Config.GetStringAsync(entryId, SinusBotConfigKeys.InstanceId);
		var instanceName = await context.Config.GetStringAsync(entryId, SinusBotConfigKeys.InstanceName);

		if (string.IsNullOrEmpty(serverUrl) ||
			string.IsNullOrEmpty(username) ||
			string.IsNullOrEmpty(password) ||
			string.IsNullOrEmpty(instanceId))
		{
			_logger.Warning("SinusBot config entry {EntryId} is incomplete; skipping", entryId);
			return null;
		}

		var client = new SinusBotClient(serverUrl);
		try
		{
			await client.AuthenticateAsync(username, password, CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex,
				"Could not connect SinusBot entry {EntryId} ({ServerUrl}) yet; will retry while polling",
				entryId,
				serverUrl);
		}

		var player = new SinusBotMusicPlayer();
		player.Connect(client, instanceId, instanceName ?? instanceId);
		return player;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(SinusBotIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("sinusbot-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private static string UniqueKey(string title, HashSet<string> used)
	{
		var baseKey = Slugify(title);
		if (string.IsNullOrEmpty(baseKey))
		{
			baseKey = "instance";
		}

		var key = baseKey;
		var suffix = 2;
		while (!used.Add(key))
		{
			key = $"{baseKey}_{suffix++}";
		}

		return key;
	}

	internal static string Slugify(string title)
	{
		var builder = new StringBuilder(title.Length + 4);
		foreach (var ch in title.ToLowerInvariant())
		{
			switch (ch)
			{
				case 'ä':
					builder.Append("ae");
					break;
				case 'ö':
					builder.Append("oe");
					break;
				case 'ü':
					builder.Append("ue");
					break;
				case 'ß':
					builder.Append("ss");
					break;
				case >= 'a' and <= 'z':
				case >= '0' and <= '9':
					builder.Append(ch);
					break;
				default:
					builder.Append('_');
					break;
			}
		}

		return CollapseUnderscoresRegex().Replace(builder.ToString(), "_").Trim('_');
	}

	private sealed record Instance(string EntryId, string Title, string Key, SinusBotMusicPlayer Player);

	[GeneratedRegex("_+")]
	private static partial Regex CollapseUnderscoresRegex();
}

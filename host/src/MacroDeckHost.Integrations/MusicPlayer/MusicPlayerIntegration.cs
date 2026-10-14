using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.MusicPlayer;

[MacroDeckIntegration]
public sealed partial class MusicPlayerIntegration : IIntegration, ISystemIntegration, IVariableProvider
{
	public const string IntegrationId = "app.macro-deck.music-player";

	private const string Prefix = "music-player-";
	private const string Suffix = "-album-color";

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
	[
		Define(VariableNameTemplate.Placeholder("player"), null)
	];

	private volatile IMusicPlayerApi? _musicPlayers;

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.MusicPlayer.Name();

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public bool IsInitialized => true;

	public bool IsActive => true;

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. Players()
				.Select(player => Define($"music_player_{player.Key}_album_color",
					new VariableConfiguration(player.InstanceId, player.Name)))
		];

	public IReadOnlyList<VariableDefinition> DeclaredVariables
	{
		get
		{
			var provided = Variables;
			return provided.Count > 0 ? provided : _templateVariables;
		}
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		_musicPlayers = context.MusicPlayers;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (_musicPlayers is not { } musicPlayers
			|| !localId.StartsWith(Prefix, StringComparison.Ordinal)
			|| !localId.EndsWith(Suffix, StringComparison.Ordinal))
		{
			return VariableReading.Unavailable;
		}

		var key = localId[Prefix.Length..^Suffix.Length];
		var player = Players()
			.FirstOrDefault(candidate =>
				string.Equals(candidate.Key.Replace('_', '-'), key, StringComparison.Ordinal));
		if (player is null)
		{
			return VariableReading.Unavailable;
		}

		return await musicPlayers.GetAlbumColorAsync(player.InstanceId, cancellationToken) is { } color
			? VariableReading.Of(color)
			: VariableReading.Unavailable;
	}

	internal static string Slugify(string name)
	{
		var builder = new StringBuilder(name.Length + 4);
		foreach (var ch in name.ToLowerInvariant())
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

	private static VariableDefinition Define(string name, VariableConfiguration? configuration)
	{
		return VariableDefinition.Eager(name, VariableType.Color, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.MusicPlayer.Variables.AlbumColorDisplayName(),
				Configuration = configuration
			};
	}

	private List<Player> Players()
	{
		var players = new List<Player>();
		if (_musicPlayers is not { } musicPlayers)
		{
			return players;
		}

		var used = new HashSet<string>(StringComparer.Ordinal);
		foreach (var instance in musicPlayers.Instances)
		{
			var baseKey = Slugify(instance.DisplayName);
			if (baseKey.Length == 0)
			{
				baseKey = "player";
			}

			var key = baseKey;
			var suffix = 2;
			while (!used.Add(key))
			{
				key = $"{baseKey}_{suffix++}";
			}

			players.Add(new Player(instance.Id, instance.DisplayName, key));
		}

		return players;
	}

	[GeneratedRegex("_+")]
	private static partial Regex CollapseUnderscoresRegex();

	private sealed record Player(string InstanceId, string Name, string Key);
}

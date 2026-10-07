using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.SoundPad;

[MacroDeckIntegration(Platforms = MacroDeckPlatform.Windows)]
public sealed class SoundPadIntegration
	: IIntegration,
		IVariableProvider,
		IConfigFlowProvider,
		IMusicPlayerProvider,
		IIntegrationIconProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.soundpad";

	internal const string InstanceId = "soundpad";

	private const string DisplayName = "SoundPad";

	private static readonly byte[] _icon = LoadIcon();

	private readonly Func<ISoundPadClient> _clientFactory;
	private readonly TimeSpan? _retryInterval;
	private readonly Lock _gate = new();

	private volatile SoundPadConnection? _connection;
	private volatile SoundPadMusicPlayer? _player;

	public SoundPadIntegration()
		: this(SoundPadClientFactory.Create)
	{
	}

	internal SoundPadIntegration(Func<ISoundPadClient> clientFactory, TimeSpan? retryInterval = null)
	{
		_clientFactory = clientFactory;
		_retryInterval = retryInterval;
		Actions = SoundPadActions.Create(IntegrationId, ResolvePlayer, GetInstances);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => DisplayName;

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/png";

	public IReadOnlyList<VariableDefinition> Variables => SoundPadVariables.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new SoundPadMacroDeck2Migration()];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new SoundPadConfigFlow(_clientFactory, () => _connection);

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> _player is null ? [] : [new MusicPlayerInstance(InstanceId, DisplayName)];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> string.Equals(instanceId, InstanceId, StringComparison.Ordinal) ? _player : null;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		Stop();

		var entries = await context.Config.GetEntriesAsync();
		if (entries.Count > 0)
		{
			var connection = new SoundPadConnection(_clientFactory, _retryInterval);
			lock (_gate)
			{
				_connection = connection;
				_player = new SoundPadMusicPlayer(connection);
			}

			connection.Start();
		}

		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		Stop();
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public void Dispose() => Stop();

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var name = localId.Replace('-', '_');
		var player = _player;

		if (name == SoundPadVariables.IsConnected)
		{
			return ValueTask.FromResult(VariableReading.Of(player?.IsConnected == true));
		}

		if (player is null || !player.LastState.IsConnected)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var state = player.LastState;
		var reading = name switch
		{
			SoundPadVariables.CurrentSound => VariableReading.Of(state.TrackName),
			SoundPadVariables.CurrentArtist => VariableReading.Of(state.Artists.Count > 0 ? state.Artists[0] : null),
			SoundPadVariables.PlaybackState => VariableReading.Of(state.PlaybackState.ToString().ToLowerInvariant()),
			SoundPadVariables.IsPlaying => VariableReading.Of(state.PlaybackState == PlaybackState.Playing),
			SoundPadVariables.Recording => VariableReading.Of(player.IsRecording),
			SoundPadVariables.Volume => VariableReading.Of(state.VolumePercent, 0, 100, 1),
			SoundPadVariables.Duration => VariableReading.Of(Seconds(state.Duration)),
			SoundPadVariables.Position => VariableReading.Of(Seconds(state.Position), 0, Seconds(state.Duration), 1),
			SoundPadVariables.IsMuted => VariableReading.Of(player.IsMuted),
			_ => VariableReading.Unavailable
		};

		return ValueTask.FromResult(reading);
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var player = _player is { IsConnected: true } connected ? connected : null;

		return localId.Replace('-', '_') switch
		{
			SoundPadVariables.Volume => MusicPlayerVariableWrites.SetVolumeAsync(player, value, cancellationToken),
			SoundPadVariables.Position => MusicPlayerVariableWrites.SeekAsync(player, value, cancellationToken),
			_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
		};
	}

	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> string.IsNullOrEmpty(instanceId) ? _player : GetPlayer(instanceId);

	private void Stop()
	{
		SoundPadConnection? connection;
		lock (_gate)
		{
			connection = _connection;
			_connection = null;
			_player = null;
		}

		connection?.Dispose();
	}

	private static int? Seconds(TimeSpan? value) => value is { } span ? (int)span.TotalSeconds : null;

	private static byte[] LoadIcon()
	{
		var assembly = typeof(SoundPadIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("soundpad-icon.png", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}

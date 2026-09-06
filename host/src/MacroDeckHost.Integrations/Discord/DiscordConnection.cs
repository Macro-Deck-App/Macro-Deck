using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord;

internal sealed class DiscordConnection : IDisposable
{
	private static readonly ILogger _logger = IntegrationLog.For<DiscordConnection>(DiscordIntegration.IntegrationId);
	private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

	private static readonly string[] _channelScopedEvents =
	[
		DiscordRpcEvents.VoiceStateCreate,
		DiscordRpcEvents.VoiceStateUpdate,
		DiscordRpcEvents.SpeakingStart,
		DiscordRpcEvents.SpeakingStop
	];

	private readonly Func<IDiscordRpcClient> _clientFactory;
	private readonly IDiscordOAuthClient _oauth;
	private readonly Func<DiscordTokens, CancellationToken, Task> _persistTokens;
	private readonly DiscordEventEmitter? _events;
	private readonly TimeSpan _reconnectDelay;
	private readonly CancellationTokenSource _cts = new();
	private readonly Lock _stateLock = new();

	private readonly Channel<Func<CancellationToken, Task>> _work =
		Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

	private readonly ConcurrentDictionary<string, string> _guildNames = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, byte> _ignoredVoiceSettings = new(StringComparer.Ordinal);

	private readonly string _clientId;
	private readonly string _clientSecret;

	private DiscordTokens _tokens;
	private IDiscordRpcClient? _client;
	private DiscordState _state = DiscordState.Disconnected;
	private string? _subscribedVoiceChannelId;
	private IReadOnlyList<string> _grantedScopes = [];
	private int _consecutiveFailures;
	private bool _disposed;

	internal DiscordConnection(
		Func<IDiscordRpcClient> clientFactory,
		IDiscordOAuthClient oauth,
		string clientId,
		string clientSecret,
		DiscordTokens tokens,
		Func<DiscordTokens, CancellationToken, Task> persistTokens,
		DiscordEventEmitter? events = null,
		TimeSpan? reconnectDelay = null)
	{
		_clientFactory = clientFactory;
		_oauth = oauth;
		_clientId = clientId;
		_clientSecret = clientSecret;
		_tokens = tokens;
		_persistTokens = persistTokens;
		_events = events;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(10);
	}

	public DiscordState State
	{
		get
		{
			lock (_stateLock)
			{
				return _state;
			}
		}
	}

	public bool IsConnected => _client?.IsConnected == true;

	public bool NeedsReauthorization { get; private set; }

	public bool AccessDenied { get; private set; }

	public bool HasIgnoredVoiceSettings => !_ignoredVoiceSettings.IsEmpty;

	public IReadOnlyList<string> IgnoredVoiceSettings
	{
		get
		{
			if (_ignoredVoiceSettings.IsEmpty)
			{
				return [];
			}

			var fieldOrder = DiscordVoiceSettingsReconciler.FieldOrder;
			return
			[
				.. _ignoredVoiceSettings.Keys
					.OrderBy(name => Array.IndexOf(fieldOrder, name))
					.ThenBy(name => name, StringComparer.Ordinal)
			];
		}
	}

	public void Start()
	{
		_ = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
		Enqueue(ConnectAsync);
	}

	public async Task<DiscordVoiceSettingsResult> SetVoiceSettingsAsync(
		DiscordVoiceSettingsPatch patch,
		CancellationToken cancellationToken = default)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			_logger.Warning("Discord command {Command} ignored: not connected", DiscordRpcCommands.SetVoiceSettings);
			return DiscordVoiceSettingsResult.NotConnected();
		}

		JsonElement response;
		try
		{
			response = await client
				.SendCommandAsync(DiscordRpcCommands.SetVoiceSettings, patch, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}
		catch (DiscordRpcException ex) when (ex.IsAuthProblem || ex.IsScopeProblem)
		{
			NeedsReauthorization = true;
			_logger.Warning(ex, "Discord rejected the voice settings command: authorization is no longer valid");
			return DiscordVoiceSettingsResult.Rejected(FormatRejection(ex));
		}
		catch (DiscordRpcException ex)
		{
			_logger.Warning(ex, "Discord command {Command} failed", DiscordRpcCommands.SetVoiceSettings);

			return ex.Code == 0
				? DiscordVoiceSettingsResult.Failed(ex.Message)
				: DiscordVoiceSettingsResult.Rejected(FormatRejection(ex));
		}
		catch (Exception ex) when (ex is TimeoutException or IOException)
		{
			_logger.Warning(ex, "Discord command {Command} failed", DiscordRpcCommands.SetVoiceSettings);
			return DiscordVoiceSettingsResult.Failed(ex.Message);
		}

		UpdateState(state => DiscordStateMapper.ApplyVoiceSettings(state, response));

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, response);
		RecordVoiceSettingsOutcome(patch, unapplied);

		return unapplied.Count == 0
			? DiscordVoiceSettingsResult.Confirmed()
			: DiscordVoiceSettingsResult.NotApplied(unapplied);
	}

	private static string FormatRejection(DiscordRpcException ex)
		=> string.Create(CultureInfo.InvariantCulture, $"{ex.Message} (code {ex.Code})");

	private void RecordVoiceSettingsOutcome(
		DiscordVoiceSettingsPatch patch,
		IReadOnlyList<DiscordUnappliedField> unapplied)
	{
		var stillUnapplied = unapplied.Select(f => f.Field).ToHashSet(StringComparer.Ordinal);

		foreach (var field in stillUnapplied)
		{
			_ignoredVoiceSettings[field] = 0;
		}

		foreach (var field in DiscordVoiceSettingsReconciler.NamedFields(patch))
		{
			if (!stillUnapplied.Contains(field))
			{
				_ignoredVoiceSettings.TryRemove(field, out _);
			}
		}
	}

	public Task JoinVoiceChannelAsync(string channelId, bool force, CancellationToken cancellationToken = default)
		=> SendAsync(DiscordRpcCommands.SelectVoiceChannel,
			new SelectVoiceChannelArgs { ChannelId = channelId, Force = force },
			onResponse: null,
			cancellationToken);

	public Task LeaveVoiceChannelAsync(CancellationToken cancellationToken = default)
		=> SendAsync(DiscordRpcCommands.SelectVoiceChannel,
			new SelectVoiceChannelArgs { ChannelId = null },
			onResponse: null,
			cancellationToken);

	public Task SelectTextChannelAsync(string channelId, CancellationToken cancellationToken = default)
		=> SendAsync(DiscordRpcCommands.SelectTextChannel,
			new SelectTextChannelArgs { ChannelId = channelId },
			onResponse: null,
			cancellationToken);

	public Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
		=> SendAsync(DiscordRpcCommands.SetActivity,
			new DiscordSetActivityArgs { Pid = Environment.ProcessId, Activity = activity },
			onResponse: null,
			cancellationToken);

	public async Task<IReadOnlyList<DiscordGuild>> GetGuildsAsync(CancellationToken cancellationToken = default)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			return [];
		}

		try
		{
			var response = await client
				.SendCommandAsync(DiscordRpcCommands.GetGuilds, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			var guilds = ReadGuilds(response);
			CacheGuildNames(guilds);
			return guilds;
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
		{
			_logger.Warning(ex, "Could not read the Discord guild list");
			return [];
		}
	}

	public async Task<IReadOnlyList<DiscordChannel>> GetChannelsAsync(
		string guildId,
		IReadOnlyCollection<int> types,
		CancellationToken cancellationToken = default)
	{
		var client = _client;
		if (client is null || !client.IsConnected || string.IsNullOrWhiteSpace(guildId))
		{
			return [];
		}

		try
		{
			var response = await client
				.SendCommandAsync(DiscordRpcCommands.GetChannels,
					new GetChannelsArgs { GuildId = guildId },
					cancellationToken: cancellationToken)
				.ConfigureAwait(false);

			return [.. ReadChannels(response).Where(c => types.Contains(c.Type))];
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
		{
			_logger.Warning(ex, "Could not read the channels of guild {GuildId}", guildId);
			return [];
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_work.Writer.TryComplete();

		DetachClient();

		lock (_stateLock)
		{
			_state = DiscordState.Disconnected;
		}

		_ignoredVoiceSettings.Clear();
		_cts.Dispose();
	}

	private static List<DiscordGuild> ReadGuilds(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("guilds", out var guilds) ||
			guilds.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<DiscordGuild>();
		foreach (var guild in guilds.EnumerateArray())
		{
			var id = DiscordStateMapper.ReadString(guild, "id");
			if (id is not null)
			{
				result.Add(new DiscordGuild(id, DiscordStateMapper.ReadString(guild, "name") ?? id));
			}
		}

		return result;
	}

	private static List<DiscordChannel> ReadChannels(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("channels", out var channels) ||
			channels.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<DiscordChannel>();
		foreach (var channel in channels.EnumerateArray())
		{
			var id = DiscordStateMapper.ReadString(channel, "id");
			if (id is not null)
			{
				result.Add(new DiscordChannel(id,
					DiscordStateMapper.ReadString(channel, "name") ?? id,
					DiscordStateMapper.ReadInt(channel, "type") ?? -1));
			}
		}

		return result;
	}

	private static Task<JsonElement> AuthenticateOnceAsync(
		IDiscordRpcClient client,
		string accessToken,
		CancellationToken cancellationToken)
		=> client.SendCommandAsync(DiscordRpcCommands.Authenticate,
			new AuthenticateArgs { AccessToken = accessToken },
			cancellationToken: cancellationToken);

	private static async Task UnsubscribeVoiceChannelAsync(
		IDiscordRpcClient client,
		string channelId,
		CancellationToken cancellationToken)
	{
		var args = new ChannelArgs { ChannelId = channelId };
		foreach (var eventName in _channelScopedEvents)
		{
			try
			{
				await client
					.SendCommandAsync(DiscordRpcCommands.Unsubscribe,
						args,
						eventName,
						cancellationToken: cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
			{
				_logger.Debug(ex, "Could not unsubscribe {Event} for channel {ChannelId}", eventName, channelId);
			}
		}
	}

	private static async Task SubscribeAsync(
		IDiscordRpcClient client,
		string eventName,
		object? args,
		CancellationToken cancellationToken)
	{
		try
		{
			await client
				.SendCommandAsync(DiscordRpcCommands.Subscribe, args, eventName, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}
		catch (DiscordRpcException ex) when (ex.IsScopeProblem)
		{
			_logger.Information("Discord did not allow subscribing to {Event}: {Message}", eventName, ex.Message);
		}
	}

	private async Task SendAsync(
		string command,
		object? args,
		Action<JsonElement>? onResponse,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			_logger.Warning("Discord command {Command} ignored: not connected", command);
			return;
		}

		try
		{
			var response = await client
				.SendCommandAsync(command, args, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			onResponse?.Invoke(response);
		}
		catch (DiscordRpcException ex) when (ex.IsAlreadyInVoiceChannel)
		{
			_logger.Information("Discord refused {Command}: already connected to a voice channel", command);
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
		{
			_logger.Warning(ex, "Discord command {Command} failed", command);
		}
	}

	private void Enqueue(Func<CancellationToken, Task> work)
	{
		if (!_work.Writer.TryWrite(work))
		{
			_logger.Debug("Discarding queued Discord work: the connection is shutting down");
		}
	}

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var work in _work.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				try
				{
					await work(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (Exception ex)
				{
					// One failed step must not take the queue down; the connection either recovers on the
					// next reconnect or reports an issue.
					_logger.Warning(ex, "Queued Discord work failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_disposed || NeedsReauthorization)
		{
			return;
		}

		DetachClient();

		var client = _clientFactory();
		client.EventReceived += OnEventReceived;
		client.Disconnected += OnDisconnected;
		_client = client;

		try
		{
			var ready = await client.ConnectAsync(_clientId, cancellationToken).ConfigureAwait(false);
			await AuthenticateAsync(client, ready, cancellationToken).ConfigureAwait(false);
			await SubscribeGlobalEventsAsync(client, cancellationToken).ConfigureAwait(false);
			await SeedVoiceSettingsAsync(client, cancellationToken).ConfigureAwait(false);
			await ResubscribeVoiceChannelAsync(client, channelIdHint: null, cancellationToken).ConfigureAwait(false);

			UpdateState(state => state with { IsConnected = true });

			_consecutiveFailures = 0;
			AccessDenied = false;
			_logger.Information("Connected to Discord as {User}", State.UserName ?? "unknown user");
		}
		catch (DiscordIpcUnavailableException ex)
		{
			AccessDenied = ex.AccessDenied;
			_logger.Debug("Discord is not reachable: {Reason}", ex.Message);
			DetachClient();
			ScheduleReconnect();
		}
		catch (DiscordOAuthException ex)
		{
			_logger.Warning(ex, "Discord authorization is no longer valid");
			NeedsReauthorization = true;
			DetachClient();
		}
		catch (DiscordRpcException ex) when (ex.IsAuthProblem || ex.IsScopeProblem)
		{
			_logger.Warning(ex, "Discord rejected the stored authorization");
			NeedsReauthorization = true;
			DetachClient();
		}
		catch (OperationCanceledException)
		{
			DetachClient();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Connecting to Discord failed; retrying in {Delay}s", NextDelay().TotalSeconds);
			DetachClient();
			ScheduleReconnect();
		}
	}

	private async Task AuthenticateAsync(
		IDiscordRpcClient client,
		JsonElement ready,
		CancellationToken cancellationToken)
	{
		var tokens = await EnsureFreshTokensAsync(cancellationToken).ConfigureAwait(false);

		JsonElement authenticated;
		try
		{
			authenticated = await AuthenticateOnceAsync(client, tokens.AccessToken, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (DiscordRpcException ex) when (ex.IsAuthProblem && _tokens.RefreshToken is not null)
		{
			_logger.Information("Discord rejected the access token; refreshing once and retrying");
			var refreshed = await RefreshTokensAsync(cancellationToken).ConfigureAwait(false);
			authenticated = await AuthenticateOnceAsync(client, refreshed.AccessToken, cancellationToken)
				.ConfigureAwait(false);
		}

		_grantedScopes = DiscordStateMapper.ReadScopes(authenticated);

		var (id, name) = DiscordStateMapper.ReadUser(authenticated);
		if (id is null)
		{
			(id, name) = DiscordStateMapper.ReadUser(ready);
		}

		// The user id has to land before anything is seeded: it is how a voice state is recognised as ours.
		// IsConnected is deliberately not set here, see the end of ConnectAsync.
		UpdateState(state => state with
		{
			UserId = id ?? state.UserId,
			UserName = name ?? state.UserName
		});
	}

	private async Task<DiscordTokens> EnsureFreshTokensAsync(CancellationToken cancellationToken)
		=> !_tokens.NeedsRefresh(DateTimeOffset.UtcNow) || _tokens.RefreshToken is null
			? _tokens
			: await RefreshTokensAsync(cancellationToken).ConfigureAwait(false);

	private async Task<DiscordTokens> RefreshTokensAsync(CancellationToken cancellationToken)
	{
		var refreshToken = _tokens.RefreshToken ??
			throw new DiscordOAuthException("No refresh token is stored; run the Discord setup again.");

		var refreshed = await _oauth
			.RefreshAsync(_clientId, _clientSecret, refreshToken, cancellationToken)
			.ConfigureAwait(false);

		_tokens = refreshed with { RefreshToken = refreshed.RefreshToken ?? refreshToken };
		await _persistTokens(_tokens, cancellationToken).ConfigureAwait(false);
		return _tokens;
	}

	private async Task SubscribeGlobalEventsAsync(IDiscordRpcClient client, CancellationToken cancellationToken)
	{
		await SubscribeAsync(client, DiscordRpcEvents.VoiceSettingsUpdate, args: null, cancellationToken)
			.ConfigureAwait(false);
		await SubscribeAsync(client, DiscordRpcEvents.VoiceChannelSelect, args: null, cancellationToken)
			.ConfigureAwait(false);
		await SubscribeAsync(client, DiscordRpcEvents.VoiceConnectionStatus, args: null, cancellationToken)
			.ConfigureAwait(false);

		if (_grantedScopes.Contains(DiscordScopes.RpcNotificationsRead, StringComparer.Ordinal))
		{
			await SubscribeAsync(client, DiscordRpcEvents.NotificationCreate, args: null, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private async Task SeedVoiceSettingsAsync(IDiscordRpcClient client, CancellationToken cancellationToken)
	{
		var settings = await client
			.SendCommandAsync(DiscordRpcCommands.GetVoiceSettings, cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		UpdateState(state => DiscordStateMapper.ApplyVoiceSettings(state, settings));

		_ignoredVoiceSettings.Clear();
	}

	private async Task ResubscribeVoiceChannelAsync(
		IDiscordRpcClient client,
		string? channelIdHint,
		CancellationToken cancellationToken)
	{
		var selected = await client
			.SendCommandAsync(DiscordRpcCommands.GetSelectedVoiceChannel, cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		var queriedId = DiscordStateMapper.ReadString(selected, "id");
		var channelId = channelIdHint ?? queriedId;

		if (_subscribedVoiceChannelId is not null &&
			!string.Equals(_subscribedVoiceChannelId, channelId, StringComparison.Ordinal))
		{
			await UnsubscribeVoiceChannelAsync(client, _subscribedVoiceChannelId, cancellationToken)
				.ConfigureAwait(false);
			_subscribedVoiceChannelId = null;
		}

		if (channelId is null)
		{
			UpdateState(state => state.WithoutVoiceChannel());
			return;
		}

		if (string.Equals(queriedId, channelId, StringComparison.Ordinal))
		{
			var guildId = DiscordStateMapper.ReadString(selected, "guild_id");
			var guildName = guildId is null
				? null
				: await ResolveGuildNameAsync(client, guildId, cancellationToken).ConfigureAwait(false);

			UpdateState(state => DiscordStateMapper.ApplySelectedVoiceChannel(state, selected, guildName));
		}

		if (_subscribedVoiceChannelId is null)
		{
			var args = new ChannelArgs { ChannelId = channelId };
			foreach (var eventName in _channelScopedEvents)
			{
				await SubscribeAsync(client, eventName, args, cancellationToken).ConfigureAwait(false);
			}

			_subscribedVoiceChannelId = channelId;
		}
	}

	private async Task<string?> ResolveGuildNameAsync(
		IDiscordRpcClient client,
		string guildId,
		CancellationToken cancellationToken)
	{
		if (_guildNames.TryGetValue(guildId, out var cached))
		{
			return cached;
		}

		try
		{
			CacheGuildNames(ReadGuilds(await client
				.SendCommandAsync(DiscordRpcCommands.GetGuilds, cancellationToken: cancellationToken)
				.ConfigureAwait(false)));
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException)
		{
			_logger.Debug(ex, "Could not resolve the name of guild {GuildId}", guildId);
			return null;
		}

		return _guildNames.TryGetValue(guildId, out var name) ? name : null;
	}

	private void CacheGuildNames(IReadOnlyList<DiscordGuild> guilds)
	{
		foreach (var guild in guilds)
		{
			_guildNames[guild.Id] = guild.Name;
		}
	}

	private void OnEventReceived(object? sender, DiscordRpcEventArgs e)
	{
		switch (e.EventName)
		{
			case DiscordRpcEvents.VoiceSettingsUpdate:
				UpdateState(state => DiscordStateMapper.ApplyVoiceSettings(state, e.Data));
				break;

			case DiscordRpcEvents.VoiceStateCreate:
			case DiscordRpcEvents.VoiceStateUpdate:
				UpdateState(state => DiscordStateMapper.ApplyVoiceState(state, e.Data));
				break;

			case DiscordRpcEvents.VoiceConnectionStatus:
				UpdateState(state => DiscordStateMapper.ApplyVoiceConnectionStatus(state, e.Data));
				break;

			case DiscordRpcEvents.SpeakingStart:
				HandleSpeaking(e.Data, speaking: true);
				break;

			case DiscordRpcEvents.SpeakingStop:
				HandleSpeaking(e.Data, speaking: false);
				break;

			case DiscordRpcEvents.VoiceChannelSelect:
				// Needs round trips (unsubscribe, query, subscribe), so it cannot run on the read loop.
				var channelId = DiscordStateMapper.ReadString(e.Data, "channel_id");
				Enqueue(ct => OnVoiceChannelSelectedAsync(channelId, ct));
				break;

			case DiscordRpcEvents.NotificationCreate:
				_events?.PublishNotification(e.Data);
				break;

			default:
				_logger.Verbose("Ignoring Discord event {Event}", e.EventName);
				break;
		}
	}

	private async Task OnVoiceChannelSelectedAsync(string? channelId, CancellationToken cancellationToken)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			return;
		}

		if (channelId is null)
		{
			if (_subscribedVoiceChannelId is not null)
			{
				await UnsubscribeVoiceChannelAsync(client, _subscribedVoiceChannelId, cancellationToken)
					.ConfigureAwait(false);
				_subscribedVoiceChannelId = null;
			}

			UpdateState(state => state.WithoutVoiceChannel());
			return;
		}

		await ResubscribeVoiceChannelAsync(client, channelId, cancellationToken).ConfigureAwait(false);
	}

	private void HandleSpeaking(JsonElement data, bool speaking)
	{
		var userId = DiscordStateMapper.ReadString(data, "user_id");
		if (userId is null)
		{
			return;
		}

		var isSelf = string.Equals(userId, State.UserId, StringComparison.Ordinal);
		if (isSelf)
		{
			UpdateState(current => current with { SelfSpeaking = speaking });
		}

		_events?.PublishSpeaking(userId, isSelf, speaking);
	}

	private void OnDisconnected(object? sender, string? reason)
	{
		if (!ReferenceEquals(sender, _client))
		{
			return;
		}

		UpdateState(_ => DiscordState.Disconnected);
		_events?.Reset();
		_subscribedVoiceChannelId = null;
		_ignoredVoiceSettings.Clear();

		if (_disposed || _cts.IsCancellationRequested || NeedsReauthorization)
		{
			return;
		}

		_logger.Information("Disconnected from Discord ({Reason}); retrying in {Delay}s",
			reason ?? "unknown",
			NextDelay().TotalSeconds);
		ScheduleReconnect();
	}

	private void ScheduleReconnect()
	{
		var delay = NextDelay();
		_consecutiveFailures++;

		Enqueue(async ct =>
		{
			await Task.Delay(delay, ct).ConfigureAwait(false);
			await ConnectAsync(ct).ConfigureAwait(false);
		});
	}

	private TimeSpan NextDelay()
	{
		var multiplier = 1L << Math.Min(_consecutiveFailures, 4);
		var delay = _reconnectDelay * multiplier;
		return delay > _maxReconnectDelay ? _maxReconnectDelay : delay;
	}

	private void DetachClient()
	{
		var client = _client;
		if (client is null)
		{
			return;
		}

		_client = null;
		client.EventReceived -= OnEventReceived;
		client.Disconnected -= OnDisconnected;
		client.Dispose();
	}

	private void UpdateState(Func<DiscordState, DiscordState> transition)
	{
		lock (_stateLock)
		{
			var updated = transition(_state);
			if (updated == _state)
			{
				return;
			}

			_state = updated;
			_events?.Observe(updated);
		}
	}

	private sealed record AuthenticateArgs
	{
		[JsonPropertyName("access_token")]
		public required string AccessToken { get; init; }
	}

	private sealed record SelectVoiceChannelArgs
	{
		[JsonPropertyName("channel_id")]
		[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
		public string? ChannelId { get; init; }

		[JsonPropertyName("force")]
		public bool? Force { get; init; }
	}

	private sealed record SelectTextChannelArgs
	{
		[JsonPropertyName("channel_id")]
		public required string ChannelId { get; init; }
	}

	private sealed record GetChannelsArgs
	{
		[JsonPropertyName("guild_id")]
		public required string GuildId { get; init; }
	}

	private sealed record ChannelArgs
	{
		[JsonPropertyName("channel_id")]
		public required string ChannelId { get; init; }
	}
}

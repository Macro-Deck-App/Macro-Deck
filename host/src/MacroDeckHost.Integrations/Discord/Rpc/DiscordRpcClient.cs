using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordRpcClient : IDiscordRpcClient
{
	// Hardcoded READY user of arRPC, its forks and rsRPC: Rich Presence-only servers that bind the Discord IPC names.
	internal const string RichPresenceServerUserId = "1045800378228281345";

	private static readonly ILogger _logger = IntegrationLog.For<DiscordRpcClient>(DiscordIntegration.IntegrationId);
	private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _defaultProbeTimeout = TimeSpan.FromSeconds(3);

	internal static JsonSerializerOptions SerializerOptions { get; } = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly Func<IDiscordIpcTransport> _transportFactory;
	private readonly TimeSpan _probeTimeout;
	private readonly CancellationTokenSource _cts = new();
	private readonly object _gate = new();

	private Connection? _connecting;
	private Connection? _live;
	private bool _disposed;

	public DiscordRpcClient(Func<IDiscordIpcTransport> transportFactory)
		: this(transportFactory, _defaultProbeTimeout)
	{
	}

	internal DiscordRpcClient(Func<IDiscordIpcTransport> transportFactory, TimeSpan probeTimeout)
	{
		_transportFactory = transportFactory;
		_probeTimeout = probeTimeout;
	}

	public event EventHandler<DiscordRpcEventArgs>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => !_disposed && _live is { IsConnected: true };

	public async Task<JsonElement> ConnectAsync(string clientId, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_live is not null)
		{
			throw new InvalidOperationException("The client is already connected.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var skipped = new HashSet<string>(StringComparer.Ordinal);

		while (true)
		{
			var connection = new Connection(this, _transportFactory());
			lock (_gate)
			{
				if (_disposed)
				{
					connection.Dispose();
					throw new ObjectDisposedException(nameof(DiscordRpcClient));
				}

				_connecting = connection;
			}

			var attached = false;
			try
			{
				var endpoint = await OpenAsync(connection.Transport, skipped, linked.Token).ConfigureAwait(false);
				var ready = await connection.StartAsync(clientId, linked.Token).ConfigureAwait(false);

				if (await IsRichPresenceOnlyAsync(connection, ready, linked.Token).ConfigureAwait(false))
				{
					_logger.Information("Skipping Discord IPC endpoint {Endpoint}: it only serves Rich Presence", endpoint);
					skipped.Add(endpoint);
					continue;
				}

				lock (_gate)
				{
					ObjectDisposedException.ThrowIf(_disposed, this);
					connection.Attach();
					_live = connection;
					attached = true;
				}

				return ready;
			}
			finally
			{
				lock (_gate)
				{
					if (ReferenceEquals(_connecting, connection))
					{
						_connecting = null;
					}
				}

				if (!attached)
				{
					connection.Dispose();
				}
			}
		}
	}

	public Task<JsonElement> SendCommandAsync(
		string command,
		object? args = null,
		string? eventName = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var connection = _live;
		return connection is null
			? throw new DiscordRpcException("Not connected to Discord.")
			: connection.SendCommandAsync(command, args, eventName, timeout, cancellationToken);
	}

	public void Dispose()
	{
		Connection? connecting;
		Connection? live;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			connecting = _connecting;
			live = _live;
		}

		try
		{
			_cts.Cancel();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while cancelling the Discord RPC client");
		}

		connecting?.Dispose();
		live?.Dispose();
		_cts.Dispose();
	}

	private static async Task<string> OpenAsync(
		IDiscordIpcTransport transport,
		HashSet<string> skipped,
		CancellationToken cancellationToken)
	{
		try
		{
			await transport.ConnectAsync(skipped, cancellationToken).ConfigureAwait(false);
		}
		catch (DiscordIpcUnavailableException ex) when (!ex.AccessDenied && skipped.Count > 0)
		{
			throw new DiscordIpcUnavailableException(
				"Only Discord Rich Presence servers answered; the Discord client itself was not found.",
				richPresenceOnly: true);
		}

		var endpoint = transport.Endpoint ?? string.Empty;
		return skipped.Contains(endpoint)
			? throw new DiscordIpcUnavailableException($"The Discord IPC endpoint {endpoint} was already skipped.")
			: endpoint;
	}

	private async Task<bool> IsRichPresenceOnlyAsync(
		Connection connection,
		JsonElement ready,
		CancellationToken cancellationToken)
	{
		if (string.Equals(DiscordStateMapper.ReadUser(ready).Id, RichPresenceServerUserId, StringComparison.Ordinal))
		{
			return true;
		}

		// The real client answers any command before AUTHENTICATE at once, usually with 4006. Only an explicit
		// unknown-command reply counts as proof, so a slow or unexpected answer keeps the endpoint.
		try
		{
			await connection
				.SendCommandAsync(DiscordRpcCommands.GetGuilds, new { }, null, _probeTimeout, cancellationToken)
				.ConfigureAwait(false);
			return false;
		}
		catch (DiscordRpcException ex) when (ex.IsUnknownCommand)
		{
			return true;
		}
		catch (DiscordRpcException ex) when (ex.Code != 0)
		{
			return false;
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	private static async Task<JsonElement> AwaitWithTimeoutAsync(
		Task<JsonElement> task,
		TimeSpan timeout,
		string what,
		CancellationToken cancellationToken)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);

		try
		{
			return await task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException(string.Create(CultureInfo.InvariantCulture,
				$"Discord did not answer {what} within {timeout.TotalSeconds:0.#}s."));
		}
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private sealed class Connection : IDisposable
	{
		private readonly DiscordRpcClient _owner;
		private readonly CancellationTokenSource _cts = new();

		private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending =
			new(StringComparer.Ordinal);

		private readonly TaskCompletionSource<JsonElement> _ready =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly object _stateGate = new();

		private bool _attached;
		private int _disconnectSignalled;
		private int _disposed;

		public Connection(DiscordRpcClient owner, IDiscordIpcTransport transport)
		{
			_owner = owner;
			Transport = transport;
		}

		public IDiscordIpcTransport Transport { get; }

		private bool IsAttached
		{
			get
			{
				lock (_stateGate)
				{
					return _attached;
				}
			}
		}

		public bool IsConnected => Transport.IsConnected && _disconnectSignalled == 0 && _disposed == 0;

		public async Task<JsonElement> StartAsync(string clientId, CancellationToken cancellationToken)
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

			var handshake = JsonSerializer.SerializeToUtf8Bytes(new HandshakePayload(1, clientId), SerializerOptions);
			await Transport.WriteFrameAsync(DiscordRpcOpcode.Handshake, handshake, linked.Token).ConfigureAwait(false);

			_ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);

			return await AwaitWithTimeoutAsync(_ready.Task, _defaultCommandTimeout, "handshake", linked.Token)
				.ConfigureAwait(false);
		}

		public void Attach()
		{
			lock (_stateGate)
			{
				if (!IsConnected)
				{
					throw new DiscordRpcException("The Discord connection ended while connecting.");
				}

				_attached = true;
			}
		}

		public async Task<JsonElement> SendCommandAsync(
			string command,
			object? args,
			string? eventName,
			TimeSpan? timeout,
			CancellationToken cancellationToken)
		{
			if (!Transport.IsConnected)
			{
				throw new DiscordRpcException("Not connected to Discord.");
			}

			var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
			var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending[nonce] = completion;

			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

			try
			{
				var payload = JsonSerializer.SerializeToUtf8Bytes(new CommandPayload(command, args, eventName, nonce),
					SerializerOptions);
				await Transport.WriteFrameAsync(DiscordRpcOpcode.Frame, payload, linked.Token).ConfigureAwait(false);

				return await AwaitWithTimeoutAsync(completion.Task,
						timeout ?? _defaultCommandTimeout,
						command,
						linked.Token)
					.ConfigureAwait(false);
			}
			finally
			{
				_pending.TryRemove(nonce, out _);
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
			{
				return;
			}

			try
			{
				_cts.Cancel();
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while cancelling the Discord RPC connection");
			}

			Transport.Dispose();
			FailPending(new DiscordRpcException("The Discord connection was closed."));
			_cts.Dispose();
		}

		private async Task ReadLoopAsync(CancellationToken cancellationToken)
		{
			string? reason = null;

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					var frame = await Transport.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
					if (frame is null)
					{
						reason = "Discord closed the connection";
						break;
					}

					reason = await HandleFrameAsync(frame.Value, cancellationToken).ConfigureAwait(false);
					if (reason is not null)
					{
						break;
					}
				}
			}
			catch (OperationCanceledException)
			{
				reason = "shutting down";
			}
			catch (Exception ex)
			{
				reason = ex.Message;
				_logger.Debug(ex, "Discord RPC read loop ended");
			}

			SignalDisconnected(reason);
		}

		private async Task<string?> HandleFrameAsync(DiscordIpcFrame frame, CancellationToken cancellationToken)
		{
			switch (frame.Opcode)
			{
				case DiscordRpcOpcode.Ping:
					await Transport.WriteFrameAsync(DiscordRpcOpcode.Pong, frame.Payload, cancellationToken)
						.ConfigureAwait(false);
					return null;

				case DiscordRpcOpcode.Pong:
					return null;

				case DiscordRpcOpcode.Close:
					return DescribeClose(frame.Payload);

				case DiscordRpcOpcode.Handshake:
				case DiscordRpcOpcode.Frame:
				default:
					DispatchFrame(frame.Payload);
					return null;
			}
		}

		private void DispatchFrame(byte[] payload)
		{
			JsonDocument document;
			try
			{
				document = JsonDocument.Parse(payload);
			}
			catch (JsonException ex)
			{
				_logger.Warning(ex, "Discarding malformed Discord RPC frame");
				return;
			}

			using (document)
			{
				var root = document.RootElement;
				var eventName = ReadString(root, "evt");
				var nonce = ReadString(root, "nonce");
				var data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;

				if (string.Equals(eventName, DiscordRpcEvents.Ready, StringComparison.Ordinal))
				{
					_ready.TrySetResult(data);
					return;
				}

				if (nonce is not null)
				{
					if (_pending.TryRemove(nonce, out var completion))
					{
						if (string.Equals(eventName, DiscordRpcEvents.Error, StringComparison.Ordinal))
						{
							completion.TrySetException(ToRpcException(data));
						}
						else
						{
							completion.TrySetResult(data);
						}
					}

					return;
				}

				if (string.Equals(eventName, DiscordRpcEvents.Error, StringComparison.Ordinal))
				{
					var error = ToRpcException(data);
					_ready.TrySetException(error);
					_logger.Warning("Discord rejected the connection: {Message}", error.Message);
					return;
				}

				if (eventName is not null && Volatile.Read(ref _disconnectSignalled) == 0 && IsAttached)
				{
					_owner.EventReceived?.Invoke(_owner, new DiscordRpcEventArgs(eventName, data));
				}
			}
		}

		private void SignalDisconnected(string? reason)
		{
			bool attached;
			lock (_stateGate)
			{
				if (Interlocked.Exchange(ref _disconnectSignalled, 1) != 0)
				{
					return;
				}

				attached = _attached;
			}

			FailPending(new DiscordRpcException($"The Discord connection ended: {reason ?? "unknown"}"));
			if (attached)
			{
				_owner.Disconnected?.Invoke(_owner, reason);
			}
		}

		private void FailPending(Exception exception)
		{
			if (_ready.TrySetException(exception))
			{
				_ = _ready.Task.Exception;
			}

			foreach (var nonce in _pending.Keys)
			{
				if (_pending.TryRemove(nonce, out var completion) && completion.TrySetException(exception))
				{
					_ = completion.Task.Exception;
				}
			}
		}

		private static DiscordRpcException ToRpcException(JsonElement data)
		{
			if (data.ValueKind != JsonValueKind.Object)
			{
				return new DiscordRpcException("Discord returned an unspecified error.");
			}

			var code = data.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed)
				? parsed
				: 0;
			var message = ReadString(data, "message") ?? "Discord returned an error.";
			return new DiscordRpcException(code, message);
		}

		private static string DescribeClose(byte[] payload)
		{
			if (payload.Length == 0)
			{
				return "Discord closed the connection";
			}

			try
			{
				using var document = JsonDocument.Parse(payload);
				var message = ReadString(document.RootElement, "message");
				return string.IsNullOrWhiteSpace(message) ? "Discord closed the connection" : message;
			}
			catch (JsonException)
			{
				return "Discord closed the connection";
			}
		}
	}

	private sealed record HandshakePayload(
		[property: JsonPropertyName("v")] int Version,
		[property: JsonPropertyName("client_id")]
		string ClientId);

	private sealed record CommandPayload(
		[property: JsonPropertyName("cmd")] string Cmd,
		[property: JsonPropertyName("args")] object? Args,
		[property: JsonPropertyName("evt")] string? Evt,
		[property: JsonPropertyName("nonce")] string Nonce);
}

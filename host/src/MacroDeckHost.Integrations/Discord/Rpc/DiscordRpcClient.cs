using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordRpcClient : IDiscordRpcClient
{
	private static readonly ILogger _logger = IntegrationLog.For<DiscordRpcClient>(DiscordIntegration.IntegrationId);
	private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(10);

	internal static JsonSerializerOptions SerializerOptions { get; } = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IDiscordIpcTransport _transport;
	private readonly CancellationTokenSource _cts = new();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending =
		new(StringComparer.Ordinal);

	private readonly TaskCompletionSource<JsonElement> _ready =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private int _disconnectSignalled;
	private bool _disposed;

	public DiscordRpcClient(IDiscordIpcTransport transport)
	{
		_transport = transport;
	}

	public event EventHandler<DiscordRpcEventArgs>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _transport.IsConnected && _disconnectSignalled == 0 && !_disposed;

	public async Task<JsonElement> ConnectAsync(string clientId, CancellationToken cancellationToken)
	{
		await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

		var handshake = JsonSerializer.SerializeToUtf8Bytes(new HandshakePayload(1, clientId), SerializerOptions);
		await _transport.WriteFrameAsync(DiscordRpcOpcode.Handshake, handshake, linked.Token).ConfigureAwait(false);

		_ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);

		return await AwaitWithTimeoutAsync(_ready.Task, _defaultCommandTimeout, "handshake", linked.Token)
			.ConfigureAwait(false);
	}

	public async Task<JsonElement> SendCommandAsync(
		string command,
		object? args = null,
		string? eventName = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		if (!_transport.IsConnected)
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
			await _transport.WriteFrameAsync(DiscordRpcOpcode.Frame, payload, linked.Token).ConfigureAwait(false);

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
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			_cts.Cancel();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while cancelling the Discord RPC client");
		}

		_transport.Dispose();
		FailPending(new DiscordRpcException("The Discord connection was closed."));
		_cts.Dispose();
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

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		string? reason = null;

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var frame = await _transport.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
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
				await _transport.WriteFrameAsync(DiscordRpcOpcode.Pong, frame.Payload, cancellationToken)
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

			if (nonce is not null && _pending.TryRemove(nonce, out var completion))
			{
				if (string.Equals(eventName, DiscordRpcEvents.Error, StringComparison.Ordinal))
				{
					completion.TrySetException(ToRpcException(data));
				}
				else
				{
					completion.TrySetResult(data);
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

			if (eventName is not null)
			{
				EventReceived?.Invoke(this, new DiscordRpcEventArgs(eventName, data));
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

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private void SignalDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectSignalled, 1) != 0)
		{
			return;
		}

		FailPending(new DiscordRpcException($"The Discord connection ended: {reason ?? "unknown"}"));
		Disconnected?.Invoke(this, reason);
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

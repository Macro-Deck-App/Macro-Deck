using System.Text.Json;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

internal sealed record RpcCall(string Command, string? EventName, string ArgsJson);

internal sealed class FakeDiscordRpcClient : IDiscordRpcClient
{
	private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Queue<Exception>> _failures = new(StringComparer.Ordinal);
	private readonly HashSet<string> _echoCommands = new(StringComparer.Ordinal);

	public List<RpcCall> Calls { get; } = [];

	public bool IsConnected { get; private set; }

	public bool IsDisposed { get; private set; }

	public int ConnectCount { get; private set; }

	public string ReadyData { get; set; } = """{"v":1,"user":{"id":"me","username":"tester"}}""";

	public Exception? ConnectException { get; set; }

	public event EventHandler<DiscordRpcEventArgs>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public IEnumerable<string?> SubscribedChannelIds
		=> Calls.Where(c => c.Command == "SUBSCRIBE" && c.EventName == "VOICE_STATE_UPDATE")
			.Select(c => ReadChannelId(c.ArgsJson));

	public IEnumerable<string?> UnsubscribedChannelIds
		=> Calls.Where(c => c.Command == "UNSUBSCRIBE" && c.EventName == "VOICE_STATE_UPDATE")
			.Select(c => ReadChannelId(c.ArgsJson));

	public FakeDiscordRpcClient Responds(string command, string dataJson)
	{
		_responses[command] = dataJson;
		return this;
	}

	public FakeDiscordRpcClient Fails(string command, Exception exception)
	{
		if (!_failures.TryGetValue(command, out var queue))
		{
			queue = new Queue<Exception>();
			_failures[command] = queue;
		}

		queue.Enqueue(exception);
		return this;
	}

	public FakeDiscordRpcClient RespondsWithEcho(string command)
	{
		_echoCommands.Add(command);
		return this;
	}

	public Task<JsonElement> ConnectAsync(string clientId, CancellationToken cancellationToken)
	{
		ConnectCount++;
		if (ConnectException is not null)
		{
			return Task.FromException<JsonElement>(ConnectException);
		}

		IsConnected = true;
		IsDisposed = false;
		return Task.FromResult(Parse(ReadyData));
	}

	public Task<JsonElement> SendCommandAsync(
		string command,
		object? args = null,
		string? eventName = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var argsJson = args is null ? "null" : JsonSerializer.Serialize(args, DiscordRpcClient.SerializerOptions);
		Calls.Add(new RpcCall(command, eventName, argsJson));

		if (_failures.TryGetValue(command, out var queue) && queue.Count > 0)
		{
			return Task.FromException<JsonElement>(queue.Dequeue());
		}

		var responseJson = _echoCommands.Contains(command) ? argsJson : _responses.GetValueOrDefault(command, "{}");
		return Task.FromResult(Parse(responseJson));
	}

	public void Dispose()
	{
		IsDisposed = true;
		IsConnected = false;
	}

	public void RaiseEvent(string eventName, string dataJson)
		=> EventReceived?.Invoke(this, new DiscordRpcEventArgs(eventName, Parse(dataJson)));

	public void RaiseDisconnected(string reason)
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	public IEnumerable<RpcCall> CallsTo(string command) => Calls.Where(c => c.Command == command);

	private static string? ReadChannelId(string argsJson)
	{
		using var document = JsonDocument.Parse(argsJson);
		return DiscordStateMapper.ReadString(document.RootElement, "channel_id");
	}

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}

internal sealed class FakeDiscordOAuthClient : IDiscordOAuthClient
{
	public int ExchangeCount { get; private set; }

	public int RefreshCount { get; private set; }

	public DiscordTokens Result { get; set; } =
		new("fresh-access", "fresh-refresh", DateTimeOffset.UtcNow.AddHours(1), "rpc");

	public Exception? Exception { get; set; }

	public Task<DiscordTokens> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		CancellationToken cancellationToken)
	{
		ExchangeCount++;
		return Exception is not null
			? Task.FromException<DiscordTokens>(Exception)
			: Task.FromResult(Result);
	}

	public Task<DiscordTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		RefreshCount++;
		return Exception is not null
			? Task.FromException<DiscordTokens>(Exception)
			: Task.FromResult(Result);
	}
}

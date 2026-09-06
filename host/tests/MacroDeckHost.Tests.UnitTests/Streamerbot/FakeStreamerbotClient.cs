using System.Text.Json;
using MacroDeckHost.Integrations.Streamerbot.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

internal sealed class FakeStreamerbotClient : IStreamerbotClient
{
	private readonly Lock _lock = new();

	public event EventHandler<StreamerbotEventMessage>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

	public HashSet<string> FailingRequests { get; } = new(StringComparer.Ordinal);

	public List<(string Request, IReadOnlyDictionary<string, object?>? Payload)> Requests { get; } = [];

	public StreamerbotHello? Hello { get; set; }

	public Exception? ConnectException { get; set; }

	public bool IsConnected { get; private set; }

	public int ConnectCount { get; private set; }

	public Uri? LastUri { get; private set; }

	public bool Disposed { get; private set; }

	public IEnumerable<string> RequestNames => Requests.Select(request => request.Request);

	public Task<StreamerbotHello?> ConnectAsync(Uri uri, CancellationToken cancellationToken)
	{
		LastUri = uri;
		ConnectCount++;

		if (ConnectException is { } exception)
		{
			return Task.FromException<StreamerbotHello?>(exception);
		}

		IsConnected = true;
		return Task.FromResult(Hello);
	}

	public Task<JsonElement> RequestAsync(
		string request,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			Requests.Add((request, payload));
		}

		if (!IsConnected)
		{
			return Task.FromException<JsonElement>(
				new StreamerbotRequestException($"Cannot send '{request}': not connected to Streamer.bot."));
		}

		if (FailingRequests.Contains(request))
		{
			return Task.FromException<JsonElement>(new StreamerbotRequestException(request, "refused"));
		}

		var body = Responses.GetValueOrDefault(request, """{ "status": "ok" }""");
		return Task.FromResult(Parse(body));
	}

	public Task DisconnectAsync()
	{
		IsConnected = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		Disposed = true;
		IsConnected = false;
	}

	public void RaiseEvent(string source, string type, string data = "{}")
		=> EventReceived?.Invoke(this, new StreamerbotEventMessage(source, type, Parse(data)));

	public void Drop(string reason = "dropped")
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	public IReadOnlyDictionary<string, object?>? PayloadOf(string request)
	{
		lock (_lock)
		{
			return Requests.LastOrDefault(entry =>
				string.Equals(entry.Request, request, StringComparison.Ordinal)).Payload;
		}
	}

	public static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}

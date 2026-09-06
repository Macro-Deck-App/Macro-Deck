using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

internal sealed class FakeHomeAssistantClient : IHomeAssistantClient
{
	private readonly Lock _lock = new();

	public event EventHandler<HomeAssistantEventMessage>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

	public HashSet<string> FailingRequests { get; } = new(StringComparer.Ordinal);

	public HashSet<string> TimingOutRequests { get; } = new(StringComparer.Ordinal);

	public List<(string Command, IReadOnlyDictionary<string, object?>? Payload)> Requests { get; } = [];

	public string? Version { get; set; } = "2026.8.0";

	public Exception? ConnectException { get; set; }

	public bool IsConnected { get; private set; }

	public int ConnectCount { get; private set; }

	public Uri? LastUri { get; private set; }

	public string? LastToken { get; private set; }

	public bool Disposed { get; private set; }

	public long CreatedAtMs { get; set; }

	public IEnumerable<string> RequestNames => Requests.Select(request => request.Command);

	public Task<HomeAssistantHello> ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		LastUri = uri;
		LastToken = token;
		ConnectCount++;

		if (ConnectException is { } exception)
		{
			return Task.FromException<HomeAssistantHello>(exception);
		}

		IsConnected = true;
		return Task.FromResult(new HomeAssistantHello(Version));
	}

	public Task<JsonElement> SendCommandAsync(
		string type,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			Requests.Add((type, payload));
		}

		if (!IsConnected)
		{
			return Task.FromException<JsonElement>(
				new HomeAssistantRequestException($"Cannot send '{type}': not connected to Home Assistant."));
		}

		if (TimingOutRequests.Contains(type))
		{
			return Task.FromException<JsonElement>(new TimeoutException($"'{type}' timed out."));
		}

		if (FailingRequests.Contains(type))
		{
			return Task.FromException<JsonElement>(new HomeAssistantRequestException("unauthorized", "refused"));
		}

		var body = Responses.GetValueOrDefault(type);
		return Task.FromResult(body is null ? default : Parse(body));
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

	public void RaiseEvent(string eventType, string data = "{}")
		=> EventReceived?.Invoke(this, new HomeAssistantEventMessage(eventType, Parse(data)));

	public void Drop(string reason = "dropped")
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	public IReadOnlyDictionary<string, object?>? PayloadOf(string command)
	{
		lock (_lock)
		{
			return Requests.LastOrDefault(entry => string.Equals(entry.Command, command, StringComparison.Ordinal))
				.Payload;
		}
	}

	public static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}

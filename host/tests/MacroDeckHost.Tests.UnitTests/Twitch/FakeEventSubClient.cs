using System.Text.Json;
using MacroDeckHost.Integrations.Twitch.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

internal sealed class FakeEventSubClient : ITwitchEventSubClient
{
	public event EventHandler<TwitchEventSubMessage>? MessageReceived;

	public event EventHandler<string?>? Disconnected;

	public TwitchSessionWelcome Welcome { get; set; } = new("session-1", TimeSpan.FromSeconds(30));

	public Exception? ConnectFailure { get; set; }

	public Uri? ConnectedTo { get; private set; }

	public bool IsConnected { get; private set; }

	public bool Disposed { get; private set; }

	public int DisconnectCount { get; private set; }

	public Task<TwitchSessionWelcome> ConnectAsync(Uri uri, CancellationToken cancellationToken)
	{
		ConnectedTo = uri;

		if (ConnectFailure is { } failure)
		{
			return Task.FromException<TwitchSessionWelcome>(failure);
		}

		IsConnected = true;
		return Task.FromResult(Welcome);
	}

	public Task DisconnectAsync()
	{
		DisconnectCount++;
		IsConnected = false;
		return Task.CompletedTask;
	}

	public void Dispose() => Disposed = true;

	public void Raise(TwitchEventSubMessage message) => MessageReceived?.Invoke(this, message);

	public void Drop(string? reason = "closed")
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	public static TwitchEventSubMessage Notification(
		string messageId,
		string subscriptionType,
		string eventJson = "{}",
		DateTimeOffset? timestamp = null)
		=> new(messageId,
			TwitchEventSubMessageTypes.Notification,
			timestamp ?? DateTimeOffset.UtcNow,
			subscriptionType,
			"1",
			Payload("{\"event\":" + eventJson + "}"));

	public static TwitchEventSubMessage Reconnect(string url)
		=> new("reconnect-1",
			TwitchEventSubMessageTypes.Reconnect,
			DateTimeOffset.UtcNow,
			null,
			null,
			Payload("{\"session\":{\"id\":\"session-2\",\"reconnect_url\":\"" + url + "\"}}"));

	public static TwitchEventSubMessage Revocation(string subscriptionType, string status)
		=> new("revocation-1",
			TwitchEventSubMessageTypes.Revocation,
			DateTimeOffset.UtcNow,
			subscriptionType,
			"1",
			Payload("{\"subscription\":{\"type\":\"" + subscriptionType + "\",\"status\":\"" + status + "\"}}"));

	public static TwitchEventSubMessage Keepalive()
		=> new("keepalive-1",
			TwitchEventSubMessageTypes.Keepalive,
			DateTimeOffset.UtcNow,
			null,
			null,
			Payload("{}"));

	private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

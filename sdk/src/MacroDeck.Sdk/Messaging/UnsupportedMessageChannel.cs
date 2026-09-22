using System.Text.Json;

namespace MacroDeck.Sdk.Messaging;

internal sealed class UnsupportedMessageChannel : IMessageChannel
{
	public static readonly UnsupportedMessageChannel Instance = new();

	public Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default)
		=> Task.FromException(Unsupported(topic));

	public Task SendAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
		=> Task.FromException(Unsupported(topic));

	public Task<JsonElement?> RequestAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
		=> Task.FromException<JsonElement?>(Unsupported(topic));

	public Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> Task.FromException<IAsyncDisposable>(Unsupported(topicPattern));

	public Task<IAsyncDisposable> HandleCommandsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> Task.FromException<IAsyncDisposable>(Unsupported(topic));

	public Task<IAsyncDisposable> HandleRequestsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
		CancellationToken cancellationToken = default)
		=> Task.FromException<IAsyncDisposable>(Unsupported(topic));

	private static MessageChannelException Unsupported(string? topic)
		=> new(MessageChannelErrorCode.Unsupported, topic, "This context offers no message channel.");
}

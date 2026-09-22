using System.Text.Json;

namespace MacroDeck.Sdk.Messaging;

/// <summary>
/// A channel through which plugins and integrations talk to each other by topic, without referencing
/// each other. Macro Deck brokers every message and stamps its sender.
/// <para>
/// Events reach every subscription whose pattern matches, including the sender's own, at most once and in
/// publish order per receiver. Commands and requests reach the one participant that handles the topic;
/// whoever registers a handler for a topic first keeps it until they dispose it or go away.
/// </para>
/// <para>
/// Registrations made through <see cref="IIntegrationContext.Messages" /> belong to the integration's
/// current initialization and are released when Macro Deck shuts it down or initializes it again, which
/// also happens after its configuration changed. Registrations made through an injected
/// <see cref="IMessageChannel" /> last until disposed. Handlers run on thread-pool threads.
/// </para>
/// </summary>
public interface IMessageChannel
{
	/// <summary>
	/// Publishes an event. Completes once Macro Deck accepted it, not when subscribers received it.
	/// </summary>
	/// <exception cref="MessageChannelException">
	/// The topic is invalid, the payload too large, the channel unsupported or not connected, or the
	/// sender publishes too quickly.
	/// </exception>
	Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default);

	/// <summary>Sends a command to the topic's handler and completes once the handler finished.</summary>
	/// <param name="timeout">How long to wait for the handler: at most 30 seconds, which is also the default.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout" /> is not positive or exceeds 30 seconds.</exception>
	/// <exception cref="MessageChannelException">
	/// No handler, the handler is unavailable, failed or timed out, or any failure of <see cref="PublishAsync" />.
	/// </exception>
	Task SendAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);

	/// <summary>Sends a request to the topic's handler and returns its reply, or null when it replied with nothing.</summary>
	/// <param name="timeout">How long to wait for the reply: at most 30 seconds, which is also the default.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout" /> is not positive or exceeds 30 seconds.</exception>
	/// <exception cref="MessageChannelException">The same failures as <see cref="SendAsync" />.</exception>
	Task<JsonElement?> RequestAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Subscribes to events on a topic or a <c>prefix.*</c> pattern. Dispose the result to unsubscribe.
	/// A handler that throws is logged; it does not affect other subscriptions or the publisher.
	/// </summary>
	/// <exception cref="MessageChannelException">The pattern is invalid or the channel is unsupported.</exception>
	Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Handles commands sent to <paramref name="topic" />. Dispose the result to stop handling it. A handler
	/// that throws fails the command with <see cref="MessageChannelErrorCode.HandlerFailed" />.
	/// </summary>
	/// <exception cref="MessageChannelException">
	/// The topic is invalid, already handled by another participant, or the channel is unsupported.
	/// </exception>
	Task<IAsyncDisposable> HandleCommandsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Handles requests sent to <paramref name="topic" />; the handler's return value is the reply. Dispose
	/// the result to stop handling it. A handler that throws fails the request with
	/// <see cref="MessageChannelErrorCode.HandlerFailed" />.
	/// </summary>
	/// <exception cref="MessageChannelException">The same failures as <see cref="HandleCommandsAsync" />.</exception>
	Task<IAsyncDisposable> HandleRequestsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
		CancellationToken cancellationToken = default);
}

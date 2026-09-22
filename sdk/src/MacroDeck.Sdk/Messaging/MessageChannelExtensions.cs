using System.Text.Json;

namespace MacroDeck.Sdk.Messaging;

/// <summary>
/// Typed forms of <see cref="IMessageChannel" />. Payloads are serialized with System.Text.Json, using
/// <see cref="JsonSerializerDefaults.Web" /> unless options are given.
/// </summary>
public static class MessageChannelExtensions
{
	public static Task PublishAsync<T>(this IMessageChannel channel,
		string topic,
		T payload,
		JsonSerializerOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		return channel.PublishAsync(topic, Serialize(payload, options), cancellationToken);
	}

	public static Task SendAsync<T>(this IMessageChannel channel,
		string topic,
		T payload,
		JsonSerializerOptions? options = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		return channel.SendAsync(topic, Serialize(payload, options), timeout, cancellationToken);
	}

	/// <summary>Sends a typed request and deserializes the reply; a reply of nothing is the default of <typeparamref name="TResponse" />.</summary>
	/// <exception cref="JsonException">The reply does not fit <typeparamref name="TResponse" />.</exception>
	public static async Task<TResponse?> RequestAsync<TRequest, TResponse>(this IMessageChannel channel,
		string topic,
		TRequest request,
		JsonSerializerOptions? options = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		var reply = await channel.RequestAsync(topic, Serialize(request, options), timeout, cancellationToken)
			.ConfigureAwait(false);
		return reply is { } element ? element.Deserialize<TResponse>(options ?? MessageChannelJson.Options) : default;
	}

	public static Task<IAsyncDisposable> SubscribeAsync<T>(this IMessageChannel channel,
		string topicPattern,
		Func<T?, ChannelMessage, CancellationToken, Task> handler,
		JsonSerializerOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		ArgumentNullException.ThrowIfNull(handler);
		return channel.SubscribeAsync(topicPattern,
			(message, token) => handler(message.PayloadAs<T>(options), message, token),
			cancellationToken);
	}

	public static Task<IAsyncDisposable> HandleCommandsAsync<T>(this IMessageChannel channel,
		string topic,
		Func<T?, ChannelMessage, CancellationToken, Task> handler,
		JsonSerializerOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		ArgumentNullException.ThrowIfNull(handler);
		return channel.HandleCommandsAsync(topic,
			(message, token) => handler(message.PayloadAs<T>(options), message, token),
			cancellationToken);
	}

	public static Task<IAsyncDisposable> HandleRequestsAsync<TRequest, TResponse>(this IMessageChannel channel,
		string topic,
		Func<TRequest?, ChannelMessage, CancellationToken, Task<TResponse>> handler,
		JsonSerializerOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(channel);
		ArgumentNullException.ThrowIfNull(handler);
		return channel.HandleRequestsAsync(topic,
			async (message, token) =>
			{
				var reply = await handler(message.PayloadAs<TRequest>(options), message, token).ConfigureAwait(false);
				return Serialize(reply, options);
			},
			cancellationToken);
	}

	private static JsonElement? Serialize<T>(T value, JsonSerializerOptions? options)
		=> value is null ? null : JsonSerializer.SerializeToElement(value, options ?? MessageChannelJson.Options);
}

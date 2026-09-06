using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IEventPublisher" />. <see cref="Publish" /> never throws, matching the
/// interface's own fire-and-forget contract - a websocket callback or polling loop that publishes an
/// event must not be brought down by anything on this side of the call, including a parameter value the
/// JSON serializer happens to reject.
/// </summary>
public sealed class FakeEventPublisher : IEventPublisher
{
	private readonly ConcurrentQueue<PublishedEvent> _published = new();

	/// <summary>Every event published so far, in arrival order.</summary>
	public IReadOnlyList<PublishedEvent> Published => [.. _published];

	/// <summary>
	/// Records the call. <paramref name="parameters" /> is serialized through the same
	/// <see cref="PluginProtocolJson.Options" /> the wire protocol uses, so <see cref="Published" />
	/// reflects what a real plugin's parameters would look like once they cross the process boundary.
	/// </summary>
	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		_published.Enqueue(new PublishedEvent
		{
			EventId = eventId,
			Parameters = Serialize(parameters),
			PublishedAt = DateTimeOffset.UtcNow
		});
	}

	private static JsonElement? Serialize(IReadOnlyDictionary<string, object?>? parameters)
	{
		if (parameters is null)
		{
			return null;
		}

		try
		{
			return JsonSerializer.SerializeToElement(parameters, PluginProtocolJson.Options);
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Publish must never throw into the caller (see IEventPublisher) - a parameter value the
			// serializer rejects is recorded as absent rather than taking the caller down with it.
			return null;
		}
	}
}

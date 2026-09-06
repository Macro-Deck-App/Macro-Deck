namespace MacroDeck.Sdk.Events;

/// <summary>Publishes events for the calling integration.</summary>
/// <remarks>
/// Publishing is fire-and-forget and does not throw into the caller. Parameter keys must match the event declaration.
/// </remarks>
public interface IEventPublisher
{
	void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null);
}

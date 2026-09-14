namespace MacroDeck.Sdk.Events;

/// <summary>Publishes events for the calling integration, and reads back what the user bound to them.</summary>
/// <remarks>
/// Publishing is fire-and-forget and does not throw into the caller. Parameter keys must match the event declaration.
/// </remarks>
public interface IEventPublisher
{
	void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null);

	/// <summary>
	/// The triggers currently bound to this integration's events. Out of process this is served from the
	/// host's last push, so it is empty until the host has pushed once and stays empty on a host that
	/// predates the feature.
	/// </summary>
	IReadOnlyList<EventBinding> GetBindings() => [];

	/// <summary>
	/// Raised on a thread-pool thread after <see cref="GetBindings" /> changed for this integration, and
	/// only then: edits elsewhere in the deck do not raise it. Never raised by the default implementation.
	/// </summary>
	event Action? BindingsChanged
	{
		add { }
		remove { }
	}
}

using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The <c>host.state</c> payload pushed for <see cref="HostApis.Deck"/>: both pickers plus where
/// each connected client is, in one push, since they change together with the deck's structure.</summary>
public sealed record DeckStateDto
{
	public IReadOnlyList<DeckFolder> Folders { get; init; } = [];

	public IReadOnlyList<DeckProfile> Profiles { get; init; } = [];

	/// <summary>Where each connected client is. Absent, and so empty, from hosts that predate it.</summary>
	public IReadOnlyList<DeckClientDto> Clients { get; init; } = [];

	/// <summary>
	/// Increases with every deck push within one host run and restarts with the host; 0 from hosts that
	/// predate it. A reader applies a push only when it is higher than the last one applied in the
	/// current session, and resets that value when a session is not resumed.
	/// </summary>
	public long Revision { get; init; }
}

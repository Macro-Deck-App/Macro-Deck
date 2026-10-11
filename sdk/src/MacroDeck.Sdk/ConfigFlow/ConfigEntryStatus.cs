namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>What Macro Deck shows next to a configuration entry on the integration's page.</summary>
public enum ConfigEntryStatus
{
	/// <summary>Configured; the integration reports nothing more specific. What an entry shows when its
	/// integration does not implement <see cref="IConfigEntryLifecycle" />.</summary>
	Ready = 0,

	Connecting = 1,

	Connected = 2,

	Reconnecting = 3,

	Disconnected = 4,

	/// <summary>The entry cannot be used as stored, for example because its credentials were rejected,
	/// and the user has to run the config flow for it again.</summary>
	NeedsReconfiguration = 5
}

namespace MacroDeck.Sdk.Devices;

/// <summary>What the host did with an interaction a provider reported.</summary>
public enum DeviceInteractionStatus
{
	/// <summary>The host resolved the interaction and ran whatever it maps to, or queued it behind the
	/// UI tree of a tile a plugin or integration serves - see
	/// <see cref="IDeviceSession.SendInteractionAsync" />.</summary>
	Accepted,

	/// <summary>
	/// The host refused the interaction and executed nothing. Normal and non-fatal: the session stays
	/// open and the next valid interaction still works.
	/// </summary>
	Rejected,

	/// <summary>
	/// The kind is part of the contract but has no widget model yet. The interaction was accepted,
	/// nothing ran, and the session stays open.
	/// </summary>
	NotSupported
}

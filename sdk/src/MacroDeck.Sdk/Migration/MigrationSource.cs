namespace MacroDeck.Sdk.Migration;

/// <summary>
/// An application Macro Deck can take a setup over from.
/// </summary>
/// <remarks>
/// Deliberately a closed set rather than an open string. Reading a foreign application's format is the
/// host's own work - a migration source has to understand that application's files before any integration
/// can be asked about its actions - so a plugin naming a source the host cannot read would be describing a
/// migration nothing could ever run. New sources are added here as the host learns to read them, which is
/// additive and leaves every existing provider valid.
/// </remarks>
public enum MigrationSource
{
	MacroDeck2 = 0,

	TouchPortal = 1,

	Deckboard = 2
}

namespace MacroDeck.Sdk.Migration;

/// <summary>
/// Implemented by integrations that can take their own configuration and actions over from another
/// application. The host asks each declared migration whether it claims a foreign action, and hands the
/// claimed ones to it one at a time.
/// </summary>
/// <remarks>
/// The capability is the list rather than the translation itself, because one integration commonly reads
/// several applications - the same OBS integration can take over an OBS setup from Macro Deck 2 and from
/// Touch Portal - and each of those is a separate body of knowledge that names its own source.
/// </remarks>
public interface IMigrationProvider
{
	/// <summary>Every application this integration can take a setup over from, one entry per source.</summary>
	IReadOnlyList<IIntegrationMigration> Migrations { get; }
}

/// <summary>Takes one application's actions and settings over into this integration.</summary>
public interface IIntegrationMigration
{
	/// <summary>Which application this reads. Two migrations on one integration never share a source.</summary>
	MigrationSource Source { get; }

	/// <summary>
	/// The identifiers this claims in the source application: whatever that application uses to say which
	/// plugin an action came from. Macro Deck 2 names the assembly a button's action type lives in.
	/// </summary>
	IReadOnlyList<string> ClaimedActionSources { get; }

	/// <summary>
	/// The identifiers this claims among the source application's per-plugin settings and credentials.
	/// Deliberately separate from <see cref="ClaimedActionSources" />: an application need not name a
	/// plugin the same way in its stored settings as it does inside an action.
	/// </summary>
	IReadOnlyList<string> ClaimedSettingsSources { get; }

	/// <summary>
	/// Translates one foreign action, or answers null when there is no equivalent - the host then keeps it
	/// as a placeholder carrying the original configuration, which is better than an action that does
	/// something different from what the user set up.
	/// </summary>
	/// <remarks>
	/// Asynchronous because this contract also has to be answerable by a plugin in another process, which
	/// reaches its answer over a connection. An in-process migration whose work is pure returns a completed
	/// task and costs nothing for it.
	/// </remarks>
	Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken);

	/// <summary>
	/// Turns a foreign plugin's settings and already-decrypted credentials into configuration entries for
	/// this integration. Returns nothing when there is nothing worth carrying over, and must not return an
	/// entry that would look configured but cannot work - credentials may be absent because the user
	/// declined to decrypt them.
	/// </summary>
	Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken);
}

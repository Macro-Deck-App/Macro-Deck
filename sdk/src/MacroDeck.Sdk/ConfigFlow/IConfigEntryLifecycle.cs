using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Lets an integration take part in how Macro Deck stores its configuration entries: adjust or refuse an
/// entry before it is stored, report each entry's status, apply a change without being initialized again,
/// and keep per-entry variables in step with the stored entries. Implement it on the integration next to
/// <see cref="IConfigFlowProvider" />; every member has a default, so implement only what you need.
/// </summary>
/// <remarks>
/// <para>
/// Macro Deck calls the members one configuration change at a time per integration: prepare, store, then
/// reload and synchronize, and release the preparation last whether or not the entry was stored. An
/// integration without this interface behaves exactly like one that keeps every default: every entry is
/// usable and <see cref="ConfigEntryStatus.Ready" />, and a change initializes the integration again.
/// </para>
/// <para>
/// A plugin implementing it declares the <c>config-entries</c> capability. A Macro Deck that predates the
/// capability rejects the declaration and never calls these members.
/// </para>
/// </remarks>
public interface IConfigEntryLifecycle
{
	/// <summary>Checks and adjusts an entry about to be stored. The default stores it unchanged.</summary>
	Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(ConfigEntryPreparationResult.Accept());
	}

	/// <summary>The status Macro Deck shows for a stored entry. The default is
	/// <see cref="ConfigEntryStatus.Ready" /> for a usable entry and
	/// <see cref="ConfigEntryStatus.NeedsReconfiguration" /> otherwise.</summary>
	async Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		return await IsEntryUsableAsync(entry, cancellationToken).ConfigureAwait(false)
			? ConfigEntryStatus.Ready
			: ConfigEntryStatus.NeedsReconfiguration;
	}

	/// <summary>
	/// Whether a stored entry can be used as it is. An integration that requires configuration is turned
	/// off when none of its entries is usable, and renaming an unusable entry does not run
	/// <see cref="PrepareEntryAsync" />. The default is <c>true</c>.
	/// </summary>
	Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(true);
	}

	/// <summary>
	/// Applies a stored change without being initialized again. Return <c>true</c> when the integration
	/// picked the change up itself; <c>false</c>, the default, makes Macro Deck shut the integration down and
	/// initialize it again, as it does for an integration without this interface. A throw counts as
	/// <c>false</c>.
	/// </summary>
	Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(false);
	}

	/// <summary>Called with every stored entry after each change, for example to create the variables of a
	/// new entry and delete those of a removed one. The default does nothing.</summary>
	Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// Called with every stored entry after each change, like the overload without
	/// <paramref name="variables" />, which the default calls. <paramref name="variables" /> is this
	/// integration's own variable api and works whether or not the integration is initialized, so an
	/// integration that is off can still create the variables of a new entry and delete those of a removed one.
	/// </summary>
	/// <remarks>A Macro Deck that predates this overload calls the one without
	/// <paramref name="variables" /> instead.</remarks>
	Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		IVariableApi variables,
		CancellationToken cancellationToken)
	{
		return SynchronizeEntriesAsync(entries, cancellationToken);
	}

	/// <summary>Called once the change a preparation was for has ended, stored or not, so the integration
	/// can let go of anything it held for it. Macro Deck releases the variable names it reserved for the
	/// preparation itself. The default does nothing.</summary>
	Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}

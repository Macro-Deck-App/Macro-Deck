using MacroDeck.Sdk.Migration;

namespace MacroDeckHost.Application.Migration;

/// <summary>
/// Resolves which integration claims a foreign action or a foreign plugin's settings, for one source
/// application.
/// </summary>
/// <remarks>
/// Lookups name the source because an integration may read several - the same OBS integration takes an OBS
/// setup over from Macro Deck 2 and, later, from Touch Portal - and two applications happily use the same
/// identifier for unrelated plugins.
/// </remarks>
public interface IMigrationActionRegistry
{
	IIntegrationMigration? FindByActionSource(MigrationSource source, string actionSource);

	IIntegrationMigration? FindBySettingsSource(MigrationSource source, string settingsSource);

	/// <summary>Every source at least one integration can migrate from, for reporting what is supported.</summary>
	IReadOnlyList<MigrationSource> SupportedSources();
}

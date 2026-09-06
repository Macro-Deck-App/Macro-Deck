using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Migration;

namespace MacroDeckHost.Infrastructure.Migration;

/// <summary>
/// Finds the integration that claims a foreign action or a foreign plugin's settings, discovering the
/// capability by interface exactly as the other integration registries do (ADR 0004).
/// </summary>
/// <remarks>
/// It deliberately does <b>not</b> filter by whether the integration is enabled, which is the one place
/// this diverges from those registries. An integration with no configuration yet is disabled, and
/// migrating a foreign setup is precisely what gives it one - skipping disabled integrations here would
/// mean a fresh installation, the normal case for someone arriving from another application, could migrate
/// nothing at all.
/// </remarks>
public sealed class MigrationActionRegistry : IMigrationActionRegistry
{
	private readonly IIntegrationRegistry _integrations;

	public MigrationActionRegistry(IIntegrationRegistry integrations)
	{
		_integrations = integrations;
	}

	public IIntegrationMigration? FindByActionSource(MigrationSource source, string actionSource)
		=> string.IsNullOrWhiteSpace(actionSource)
			? null
			: Find(source,
				migration => migration.ClaimedActionSources.Contains(actionSource, StringComparer.OrdinalIgnoreCase));

	public IIntegrationMigration? FindBySettingsSource(MigrationSource source, string settingsSource)
		=> string.IsNullOrWhiteSpace(settingsSource)
			? null
			: Find(source,
				migration => migration.ClaimedSettingsSources.Contains(settingsSource,
					StringComparer.OrdinalIgnoreCase));

	public IReadOnlyList<MigrationSource> SupportedSources()
		=> Migrations().Select(migration => migration.Source).Distinct().Order().ToList();

	private IIntegrationMigration? Find(MigrationSource source, Func<IIntegrationMigration, bool> claims)
		=> Migrations().Where(migration => migration.Source == source).FirstOrDefault(claims);

	private IEnumerable<IIntegrationMigration> Migrations()
		=> _integrations.Integrations.OfType<IMigrationProvider>().SelectMany(provider => provider.Migrations);
}

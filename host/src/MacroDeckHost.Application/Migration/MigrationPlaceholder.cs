namespace MacroDeckHost.Application.Migration;

/// <summary>
/// Identity of the action that stands in for a foreign action no migrator claimed. It carries the
/// original action's identity and configuration so nothing the source knew is lost, and fails when run
/// rather than doing nothing quietly.
/// </summary>
public static class MigrationPlaceholder
{
	public const string IntegrationId = "app.macro-deck.migration";

	public const string ActionId = "unsupported-action";

	public const string SourceAppParameter = "sourceApp";

	public const string SourceActionParameter = "sourceAction";

	public const string SourceConfigurationParameter = "sourceConfiguration";
}

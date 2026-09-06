using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Migration;

/// <summary>
/// Holds the single action that stands in for anything a migration could not translate. It is a system
/// integration because a migrated deck must keep rendering its buttons whether or not anyone ever enables
/// something, and it contributes nothing else - no variables, no events, no configuration.
/// </summary>
[MacroDeckIntegration]
public sealed class MigrationIntegration : IIntegration, ISystemIntegration
{
	public const string IntegrationId = MigrationPlaceholder.IntegrationId;

	public MigrationIntegration()
	{
		Actions = [new UnsupportedMigratedActionDefinition()];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Migration.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;
}

/// <summary>
/// A foreign action that has no Macro Deck 3 equivalent, kept on the deck with everything the source knew
/// about it so the configuration can be read and rebuilt by hand.
/// </summary>
/// <remarks>
/// Running it fails rather than doing nothing. A step that quietly succeeded would make a half-migrated
/// button look like it worked, and the flow's own result reporting is what tells the user otherwise.
/// </remarks>
internal sealed class UnsupportedMigratedActionDefinition : IActionDefinition
{
	public string Id => MigrationPlaceholder.ActionId;

	public LocalizedText Name => AppStrings.Integrations.Migration.Actions.UnsupportedName();

	public LocalizedText Description => AppStrings.Integrations.Migration.Actions.UnsupportedDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(MigrationPlaceholder.SourceAppParameter,
			label: AppStrings.Integrations.Migration.Actions.SourceAppLabel()),
		ActionParameter.Text(MigrationPlaceholder.SourceActionParameter,
			label: AppStrings.Integrations.Migration.Actions.SourceActionLabel()),
		ActionParameter.Text(MigrationPlaceholder.SourceConfigurationParameter,
			label: AppStrings.Integrations.Migration.Actions.SourceConfigurationLabel())
	];

	public IActionExecutor CreateExecutor() => new Executor();

	private sealed class Executor : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var sourceAction = context.Parameters.GetValueOrDefault(MigrationPlaceholder.SourceActionParameter)
				as string;

			return Task.FromResult(ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.Migration.Errors.NotMigrated(sourceAction ?? string.Empty)));
		}
	}
}

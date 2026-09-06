using System.Diagnostics.CodeAnalysis;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal static class HomeAssistantActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		Func<HomeAssistantConnection?> resolver,
		HomeAssistantVariableAccessor variables) =>
	[
		new CallServiceActionDefinition(resolver),
		new EntityPowerActionDefinition("turn-on",
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.TurnOnName(),
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.TurnOnDescription(),
			"turn_on",
			resolver),
		new EntityPowerActionDefinition("turn-off",
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.TurnOffName(),
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.TurnOffDescription(),
			"turn_off",
			resolver),
		new EntityPowerActionDefinition("toggle",
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.ToggleName(),
			AppStrings.Integrations.HomeAssistant.Actions.EntityPower.ToggleDescription(),
			"toggle",
			resolver),
		new LightActionDefinition(resolver),
		new CoverActionDefinition(resolver),
		new MediaPlayerActionDefinition(resolver),
		new ClimateActionDefinition(resolver),
		new SceneActionDefinition(resolver),
		new ScriptActionDefinition(resolver),
		new AutomationActionDefinition(resolver),
		new GetEntityStateActionDefinition(resolver, variables)
	];
}

internal static class HomeAssistantServiceCall
{
	public static bool TryConnect(
		Func<HomeAssistantConnection?> resolver,
		[NotNullWhen(true)] out HomeAssistantConnection? connection,
		[NotNullWhen(false)] out ActionResult? rejection)
	{
		connection = resolver();
		if (connection is { IsConnected: true })
		{
			rejection = null;
			return true;
		}

		connection = null;
		rejection = ActionResult.Failed(ActionErrorCodes.NotConnected,
			AppStrings.Integrations.HomeAssistant.Errors.NotConnected());
		return false;
	}

	public static bool TryEntity(
		HomeAssistantConnection connection,
		string? entityId,
		[NotNullWhen(true)] out string? resolved,
		[NotNullWhen(false)] out ActionResult? rejection)
	{
		resolved = null;

		if (entityId is not { Length: > 0 })
		{
			rejection = ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				AppStrings.Integrations.HomeAssistant.Errors.NoEntitySelected());
			return false;
		}

		if (connection.Entity(entityId) is null)
		{
			rejection = ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.HomeAssistant.Errors.EntityUnknown(entityId: entityId));
			return false;
		}

		resolved = entityId;
		rejection = null;
		return true;
	}

	public static ActionResult? Reject(HomeAssistantConnection connection, IReadOnlyList<string> entityIds)
	{
		if (entityIds.Count == 0)
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				AppStrings.Integrations.HomeAssistant.Errors.NoEntitiesSelected());
		}

		foreach (var entityId in entityIds)
		{
			if (connection.Entity(entityId) is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.HomeAssistant.Errors.EntityUnknown(entityId: entityId));
			}
		}

		return null;
	}

	public static IReadOnlyDictionary<string, object?> Target(string entityId)
		=> new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity_id"] = entityId };

	public static async Task<ActionResult> ExecuteAsync(
		HomeAssistantConnection connection,
		string domain,
		string service,
		IReadOnlyDictionary<string, object?>? target,
		IReadOnlyDictionary<string, object?>? data,
		CancellationToken cancellationToken)
	{
		var error = await connection.CallServiceAsync(domain, service, target, data, cancellationToken);
		return error is null
			? ActionResult.Success()
			: ActionResult.Failed(ActionErrorCodes.ProviderError, error.Value);
	}
}

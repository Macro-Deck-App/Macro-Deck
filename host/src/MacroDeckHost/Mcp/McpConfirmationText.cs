using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Mcp;

public sealed class McpConfirmationText
{
	public const int MaxParameters = 5;
	public const int MaxValueLength = 60;

	private readonly McpTextResolver _text;
	private readonly IIntegrationRegistry _registry;

	public McpConfirmationText(McpTextResolver text, IIntegrationRegistry registry)
	{
		_text = text;
		_registry = registry;
	}

	public async Task<string> RiskyAction(
		string integrationId,
		string actionId,
		IReadOnlyDictionary<string, JsonElement>? parameters = null)
	{
		var action = _registry.FindAction(integrationId, actionId);
		var actionName = action is null ? actionId : await _text.Resolve(action.Name) ?? actionId;
		var provider = _registry.GetActions(false)
			.FirstOrDefault(candidate => candidate.OwnerId == integrationId)
			?.Owner;
		var providerName = provider is null ? integrationId : await _text.Resolve(provider.Name) ?? integrationId;

		var summary = action is null || parameters is null || parameters.Count == 0
			? null
			: await Summary(action, parameters);
		return summary is null
			? await _text.Resolve(AppStrings.Mcp.Elicitation.RiskyAction(actionName, providerName)) ?? actionName
			: await _text.Resolve(AppStrings.Mcp.Elicitation.RiskyActionWithValues(actionName, providerName, summary))
			?? actionName;
	}

	public async Task<string> ProtectedVariable(string name)
	{
		return await _text.Resolve(AppStrings.Mcp.Elicitation.ProtectedVariable(name)) ?? name;
	}

	public async Task<string> SteeringValue(string name)
	{
		return await _text.Resolve(AppStrings.Mcp.Elicitation.SteeringValue(name)) ?? name;
	}

	public async Task<string> For(McpConfirmation confirmation)
	{
		return confirmation.Kind switch
		{
			McpConfirmationKinds.RiskyAction when confirmation is
					{ IntegrationId: { } integrationId, ActionId: { } actionId }
				=> await RiskyAction(integrationId, actionId),
			McpConfirmationKinds.ProtectedVariable when confirmation.Name is { } name => await ProtectedVariable(name),
			McpConfirmationKinds.SteeringValue when confirmation.Name is { } name => await SteeringValue(name),
			_ => confirmation.Detail
		};
	}

	public async Task<string> Message(IEnumerable<string> details)
	{
		return await _text.Resolve(AppStrings.Mcp.Elicitation.Confirm(string.Join("; ", details))) ?? string.Empty;
	}

	public async Task<string> Title()
	{
		return await _text.Resolve(AppStrings.Mcp.Elicitation.ConfirmTitle()) ?? string.Empty;
	}

	private async Task<string?> Summary(IActionDefinition action, IReadOnlyDictionary<string, JsonElement> parameters)
	{
		var parts = new List<string>();
		foreach (var parameter in action.Parameters.Where(parameter => parameters.ContainsKey(parameter.Name))
			.Take(MaxParameters))
		{
			var label = await _text.Resolve(parameter.Label) is { Length: > 0 } resolved ? resolved : parameter.Name;
			var value = parameter.Type is ActionParameterType.Password or ActionParameterType.Secret
				? await _text.Resolve(AppStrings.Mcp.Elicitation.SecretValue()) ?? string.Empty
				: Shorten(parameters[parameter.Name]);
			parts.Add($"{label}: {value}");
		}

		return parts.Count == 0 ? null : string.Join(", ", parts);
	}

	private static string Shorten(JsonElement value)
	{
		var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
		return text.Length <= MaxValueLength ? text : text[..(MaxValueLength - 1)] + "…";
	}
}

using System.ComponentModel;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp.Tools;

[McpServerToolType]
public sealed class McpCatalogTools
{
	public const string KindActions = "actions";
	public const string KindEvents = "events";
	public const string KindWidgetTypes = "widgetTypes";

	private readonly IUiTransportMessageHandler<GetActionsRequest, GetActionsResponse> _getActions;
	private readonly IUiTransportMessageHandler<GetEventDefinitionsRequest, GetEventDefinitionsResponse> _getEvents;
	private readonly IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse> _getWidgetTypes;

	private readonly IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse>
		_getWidgetSchemas;

	private readonly McpTextResolver _text;
	private readonly IIntegrationRegistry _registry;
	private readonly IMcpRiskPolicy _risk;

	public McpCatalogTools(
		IUiTransportMessageHandler<GetActionsRequest, GetActionsResponse> getActions,
		IUiTransportMessageHandler<GetEventDefinitionsRequest, GetEventDefinitionsResponse> getEvents,
		IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse> getWidgetTypes,
		IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse> getWidgetSchemas,
		McpTextResolver text,
		IIntegrationRegistry registry,
		IMcpRiskPolicy risk)
	{
		_registry = registry;
		_risk = risk;
		_getActions = getActions;
		_getEvents = getEvents;
		_getWidgetTypes = getWidgetTypes;
		_getWidgetSchemas = getWidgetSchemas;
		_text = text;
	}

	[McpServerTool(Name = "catalog",
		Title = "Catalog",
		ReadOnly = true,
		Idempotent = true,
		UseStructuredContent = true)]
	[Description("Lists what can be built in Macro Deck: actions with their parameter schemas, events that can "
		+ "start automations, and widget types with their data schemas. Parameters marked secret take a secret "
		+ "reference created with set_secret, never a value.")]
	public async Task<McpCatalogResult> Catalog(
		[Description("Optional: actions, events or widgetTypes. Omit to get all three.")]
		string? kind = null,
		[Description("Optional: only entries of this integration or provider id.")]
		string? integrationId = null,
		CancellationToken cancellationToken = default)
	{
		if (kind is not null && kind is not (KindActions or KindEvents or KindWidgetTypes))
		{
			throw McpToolErrors.Create(McpToolErrors.InvalidArgument,
				$"kind must be one of: {KindActions}, {KindEvents}, {KindWidgetTypes}.");
		}

		var actions = kind is null or KindActions ? await Actions(integrationId, cancellationToken) : null;
		var events = kind is null or KindEvents ? await Events(integrationId, cancellationToken) : null;
		var widgetTypes = kind is null or KindWidgetTypes ? await WidgetTypes(integrationId, cancellationToken) : null;

		return new McpCatalogResult(McpContract.Version, actions, events, widgetTypes);
	}

	private async Task<IReadOnlyList<McpActionInfo>> Actions(string? integrationId, CancellationToken cancellationToken)
	{
		var response = await _getActions.Handle(new GetActionsRequest(), cancellationToken);
		var result = new List<McpActionInfo>();
		foreach (var action in response.Actions.Where(action => Matches(action.IntegrationId, integrationId)))
		{
			result.Add(new McpActionInfo(action.IntegrationId,
				action.Id,
				await _text.Resolve(action.IntegrationName),
				await _text.Resolve(action.Name),
				await _text.Resolve(action.Description),
				(_registry.FindAction(action.IntegrationId, action.Id)?.RiskCategory
					?? MacroDeck.Sdk.Actions.ActionRiskCategory.None)
				.ToString()
				.ToLowerInvariant(),
				PolicyName(await _risk.Classify(action.IntegrationId, action.Id)),
				await Parameters(action.Parameters)));
		}

		return result;
	}

	private async Task<IReadOnlyList<McpEventInfo>> Events(string? providerId, CancellationToken cancellationToken)
	{
		var response = await _getEvents.Handle(new GetEventDefinitionsRequest(), cancellationToken);
		var result = new List<McpEventInfo>();
		foreach (var definition in response.Events.Where(definition => Matches(definition.ProviderId, providerId)))
		{
			result.Add(new McpEventInfo(definition.Id,
				definition.ProviderId,
				await _text.Resolve(definition.ProviderName),
				await _text.Resolve(definition.Name),
				await _text.Resolve(definition.Description),
				definition.DeliveryKind,
				await Parameters(definition.ConfigurationParameters),
				await Parameters(definition.PayloadParameters)));
		}

		return result;
	}

	private async Task<IReadOnlyList<McpWidgetTypeInfo>> WidgetTypes(
		string? providerId,
		CancellationToken cancellationToken)
	{
		var types = await _getWidgetTypes.Handle(new GetWidgetTypesRequest(), cancellationToken);
		var schemas = await _getWidgetSchemas.Handle(new GetWidgetDataSchemasRequest(), cancellationToken);
		var result = new List<McpWidgetTypeInfo>();
		foreach (var type in types.Types.Where(type => Matches(type.ProviderId, providerId)))
		{
			JsonElement? schema = schemas.Schemas.TryGetValue(type.Id, out var found) ? found : null;
			result.Add(new McpWidgetTypeInfo(type.Id,
				type.ProviderId,
				await _text.Resolve(type.Name),
				await _text.Resolve(type.Description),
				type.DefaultData,
				schema));
		}

		return result;
	}

	private async Task<IReadOnlyList<McpParameterInfo>> Parameters(IEnumerable<ActionParameterDef> parameters)
	{
		var result = new List<McpParameterInfo>();
		foreach (var parameter in parameters)
		{
			List<McpOptionInfo>? options = null;
			if (parameter.Options is { } declared)
			{
				options = [];
				foreach (var option in declared)
				{
					options.Add(new McpOptionInfo(option.Value, await _text.Resolve(option.Label)));
				}
			}

			var secret = parameter.Type is ActionParameterType.Password or ActionParameterType.Secret;
			result.Add(new McpParameterInfo(parameter.Name,
				parameter.Type.ToString(),
				await _text.Resolve(parameter.Label),
				await _text.Resolve(parameter.Description),
				parameter.Required,
				secret,
				secret ? null : parameter.DefaultValue,
				options,
				parameter.Children is { } children ? await Parameters(children) : null));
		}

		return result;
	}

	private static string PolicyName(McpRiskMode mode)
	{
		return mode switch
		{
			McpRiskMode.Allow => "allow",
			McpRiskMode.Confirm => "ask-first",
			_ => "off"
		};
	}

	private static bool Matches(string id, string? filter)
	{
		return filter is null || string.Equals(id, filter, StringComparison.Ordinal);
	}
}

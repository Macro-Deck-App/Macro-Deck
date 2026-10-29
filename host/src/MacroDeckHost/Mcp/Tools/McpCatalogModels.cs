using System.Text.Json;

namespace MacroDeckHost.Mcp.Tools;

public sealed record McpCatalogResult(
	string ContractVersion,
	IReadOnlyList<McpActionInfo>? Actions,
	IReadOnlyList<McpEventInfo>? Events,
	IReadOnlyList<McpWidgetTypeInfo>? WidgetTypes);

public sealed record McpActionInfo(
	string IntegrationId,
	string ActionId,
	string? Integration,
	string? Name,
	string? Description,
	string RiskCategory,
	string Policy,
	IReadOnlyList<McpParameterInfo> Parameters);

public sealed record McpEventInfo(
	string Id,
	string ProviderId,
	string? Provider,
	string? Name,
	string? Description,
	string DeliveryKind,
	IReadOnlyList<McpParameterInfo> ConfigurationParameters,
	IReadOnlyList<McpParameterInfo> PayloadParameters);

public sealed record McpWidgetTypeInfo(
	string Type,
	string ProviderId,
	string? Name,
	string? Description,
	JsonElement DefaultData,
	JsonElement? DataSchema);

public sealed record McpParameterInfo(
	string Name,
	string Type,
	string? Label,
	string? Description,
	bool Required,
	bool Secret,
	JsonElement? Default,
	IReadOnlyList<McpOptionInfo>? Options,
	IReadOnlyList<McpParameterInfo>? Children);

public sealed record McpOptionInfo(string Value, string? Label);

using System.Text.Json;

namespace MacroDeckHost.Mcp.Tools;

public sealed record McpListResult(string Entity, IReadOnlyList<JsonElement> Items);

public sealed record McpGetResult(string Entity, JsonElement? Item, IReadOnlyList<McpSecretBlockStatus> SecretBlocks);

public sealed record McpSecretBlockStatus(string BlockId, bool Approved);

public sealed record McpProfileItem(string Id, string Name, int Order, int DefaultRows, int DefaultColumns);

public sealed record McpFolderItem(
	string Id,
	string Name,
	string? ProfileId,
	string? ParentId,
	int Order,
	int? Rows,
	int? Columns,
	bool IsDefault,
	int WidgetCount);

public sealed record McpWidgetItem(
	string Id,
	string FolderId,
	string Type,
	int PositionX,
	int PositionY,
	int Width,
	int Height,
	JsonElement? Data);

public sealed record McpAutomationItem(
	string Id,
	string Name,
	string Description,
	bool Enabled,
	JsonElement? Flows);

public sealed record McpScriptItem(
	string Id,
	string Name,
	string Description,
	bool RunsOnWidget,
	JsonElement Inputs,
	JsonElement? Flows);

public sealed record McpVariableItem(
	string Id,
	string Name,
	string Scope,
	string? ScopeRefId,
	string Type,
	string Classification,
	string? OwnerIntegrationId,
	string Value,
	bool CanWrite);

using System.Text.Json;

namespace MacroDeckHost.Application.Mcp;

public sealed record McpChange(
	string Op,
	string Entity,
	string? Id = null,
	string? Ref = null,
	JsonElement? Data = null);

public sealed record McpProblem(int Index, string Path, string Code, string Message);

public sealed record McpConfirmation(
	int Index,
	string Kind,
	string Detail,
	string? IntegrationId = null,
	string? ActionId = null,
	string? Name = null);

public sealed record McpChangeValidation(
	IReadOnlyList<McpProblem> Problems,
	IReadOnlyList<McpConfirmation> Confirmations)
{
	public bool Valid => Problems.Count == 0;
}

public sealed record McpChangeResult(
	bool Applied,
	Guid? BatchId,
	IReadOnlyList<McpProblem> Problems,
	IReadOnlyDictionary<string, string> CreatedIds);

public static class McpChangeOps
{
	public const string Create = "create";
	public const string Update = "update";
	public const string Delete = "delete";
}

public static class McpProblemCodes
{
	public const string InvalidArgument = "INVALID_ARGUMENT";
	public const string NotFound = "NOT_FOUND";
	public const string Unsupported = "UNSUPPORTED";
	public const string RefUnresolved = "REF_UNRESOLVED";
	public const string ActionNotFound = "ACTION_NOT_FOUND";
	public const string ParameterRequired = "PARAMETER_REQUIRED";
	public const string EventNotFound = "EVENT_NOT_FOUND";
	public const string WidgetTypeNotFound = "WIDGET_TYPE_NOT_FOUND";
	public const string InvalidData = "INVALID_DATA";
	public const string ProviderBlocked = "PROVIDER_BLOCKED";
	public const string ForeignSecretRef = "FOREIGN_SECRET_REF";
	public const string ApplyFailed = "APPLY_FAILED";
	public const string EntityChangedSince = "ENTITY_CHANGED_SINCE";
}

public static class McpConfirmationKinds
{
	public const string RiskyAction = "risky-action";
	public const string ProtectedVariable = "protected-variable";
	public const string SteeringValue = "steering-value";
}

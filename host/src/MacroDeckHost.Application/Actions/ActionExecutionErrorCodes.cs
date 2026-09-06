namespace MacroDeckHost.Application.Actions;

public static class ActionExecutionErrorCodes
{
	public const string InvalidBlock = "INVALID_BLOCK";

	public const string IntegrationDisabled = "INTEGRATION_DISABLED";

	public const string IntegrationNotFound = "INTEGRATION_NOT_FOUND";

	public const string ActionNotFound = "ACTION_NOT_FOUND";

	public const string UnsupportedBlock = "UNSUPPORTED_BLOCK";

	public const string FlowNotFound = "FLOW_NOT_FOUND";

	public const string FlowParseError = "FLOW_PARSE_ERROR";

	public const string FlowError = "FLOW_ERROR";

	public const string ActionFailed = "ACTION_FAILED";
	public const string Cancelled = "CANCELLED";
	public const string Timeout = "TIMEOUT";
	public const string ProviderUnreachable = "PROVIDER_UNREACHABLE";
	public const string PermissionDenied = "PERMISSION_DENIED";
	public const string InvalidParameter = "INVALID_PARAMETER";
	public const string Unsupported = "UNSUPPORTED";
	public const string ScriptNotFound = "SCRIPT_NOT_FOUND";
	public const string ScriptDepthExceeded = "SCRIPT_DEPTH_EXCEEDED";
	public const string ScriptInputMissing = "SCRIPT_INPUT_MISSING";
	public const string ScriptInputInvalid = "SCRIPT_INPUT_INVALID";
	public const string ScriptWidgetRequired = "SCRIPT_WIDGET_REQUIRED";

	public const string Unavailable = "UNAVAILABLE";

	public const string HostLocked = "HOST_LOCKED";
}

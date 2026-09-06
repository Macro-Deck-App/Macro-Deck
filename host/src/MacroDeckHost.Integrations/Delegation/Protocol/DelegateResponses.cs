using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Delegation.Protocol;

internal static class DelegateJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class DelegateBuildInfoDto
{
	public string? Commit { get; set; }
}

internal sealed class DelegateLoginResponseDto
{
	public string AccessToken { get; set; } = string.Empty;

	public int ExpiresInSeconds { get; set; }

	public string? Scope { get; set; }

	public string? Username { get; set; }
}

internal sealed class DelegateConnectionInfoDto
{
	public string InstanceName { get; set; } = string.Empty;

	public JsonElement Endpoints { get; set; }

	public string? Version { get; set; }
}

internal sealed class DelegateErrorDto
{
	public string? Code { get; set; }

	public string? Message { get; set; }
}

internal sealed class DelegateRunResponseDto
{
	public bool Success { get; set; }

	public DelegateErrorDto? Error { get; set; }

	public string? ExecutionId { get; set; }

	public string? Status { get; set; }

	public long? DurationMs { get; set; }

	public List<string>? AppliedInputs { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}

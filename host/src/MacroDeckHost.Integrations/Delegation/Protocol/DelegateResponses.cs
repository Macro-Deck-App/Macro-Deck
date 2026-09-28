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

internal sealed class DelegateSharedVariablesResponseDto
{
	public List<DelegateSharedVariableDto>? Variables { get; set; }
}

internal sealed class DelegateSharedVariableDto
{
	public string? Name { get; set; }

	public string? Type { get; set; }

	public string? Value { get; set; }

	public bool Present { get; set; }

	public bool Available { get; set; }

	public bool CanWrite { get; set; }

	public bool CommitOnRelease { get; set; }

	public int? DecimalPlaces { get; set; }

	public string? Unit { get; set; }

	public double? Min { get; set; }

	public double? Max { get; set; }

	public double? Step { get; set; }
}

internal sealed class DelegateWriteResponseDto
{
	public bool Success { get; set; }

	public DelegateErrorDto? Error { get; set; }
}

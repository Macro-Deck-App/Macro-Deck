using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Ui;

public static class UiWebSocketProtocol
{
	public const int Version = 1;
	public const string Path = "/ws/ui";
	public const string SubProtocol = "macrodeck.ui.v1";
	public const int MaxMessageBytes = 256 * 1024;

	public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		MaxDepth = 32
	};
}

public sealed record UiWebSocketEnvelope(
	int ProtocolVersion,
	string Kind,
	string? Type,
	string? Id,
	string? CorrelationId,
	object? Payload,
	UiWebSocketError? Error);

public sealed record UiWebSocketError(string Code, string? Message = null);

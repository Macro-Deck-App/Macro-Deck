namespace MacroDeck.Plugin.Protocol.Logging;

/// <summary>
/// One exception in a <c>log.publish</c> event's chain. Structured rather than a pre-composed
/// <c>ToString()</c> blob, so the host composes the on-disk text itself and can cap each part
/// independently.
/// </summary>
public sealed record LogExceptionDto
{
	public required string Type { get; init; }

	public required string Message { get; init; }

	public string? StackTrace { get; init; }

	public LogExceptionDto? Inner { get; init; }
}

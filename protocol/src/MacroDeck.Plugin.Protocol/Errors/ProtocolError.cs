namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// The error carried on an envelope's <c>error</c> field. Mutually exclusive with <c>payload</c> on
/// the same envelope.
/// </summary>
public sealed record ProtocolError
{
	public required string Code { get; init; }

	public required string Message { get; init; }

	public IReadOnlyDictionary<string, string>? Details { get; init; }

	public required bool Retryable { get; init; }
}

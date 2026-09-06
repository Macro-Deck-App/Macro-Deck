namespace MacroDeck.Plugin.Protocol.Logging;

/// <summary>
/// One structured log event in a <c>log.publish</c> batch. Deliberately carries nothing identifying -
/// no plugin id, integration id, plugin version or process id. The host already has all four from the
/// authenticated session and the supervisor, and a field that only ever gets discarded invites someone
/// to start trusting it.
/// </summary>
public sealed record LogEventDto
{
	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>A <see cref="LogLevels" /> value.</summary>
	public required string Level { get; init; }

	public required string MessageTemplate { get; init; }

	public required string RenderedMessage { get; init; }

	public string? SourceContext { get; init; }

	/// <summary>
	/// A flat string-to-string map, pre-rendered by the plugin - not a <c>JsonElement</c>. A nested
	/// tree would force a host-side converter recursing over attacker-controlled JSON to
	/// <c>ProtocolLimits.MaxJsonDepth</c>, which is pure attack surface, and it buys nothing because
	/// the host's on-disk line format never persists structured properties. Only scalar values
	/// rendered to strings are supported.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Properties { get; init; }

	public LogExceptionDto? Exception { get; init; }
}

namespace MacroDeck.Plugin.Protocol.Compatibility;

/// <summary>
/// The host's verdict on one plugin, returned in the session response and mirrored to the admin UI.
///
/// <para>
/// Returned at the end of the handshake rather than at registration because registration happens once
/// and carries no version data at all - protocol and capability versions are negotiated on
/// <c>POST /api/plugins/sessions</c>, so that is the only point at which the host knows enough to have
/// a verdict.
/// </para>
/// </summary>
public sealed record PluginCompatibilityReport
{
	/// <summary>One of <see cref="PluginCompatibilityStates" /> - the worst of everything in
	/// <see cref="Findings" />, plus the negotiation outcome.</summary>
	public required string State { get; init; }

	/// <summary>
	/// The evidence behind <see cref="State" />'s deprecation half, one of
	/// <see cref="CompatibilityFindingSources" />. Carried separately from the individual findings so a
	/// UI can caveat the whole report - "inferred from the SDK version" - without inspecting each row.
	/// </summary>
	public required string UsageSource { get; init; }

	public required int NegotiatedProtocolVersion { get; init; }

	/// <summary>Null when the plugin reported no SDK block at all.</summary>
	public string? SdkVersion { get; init; }

	/// <summary>True when the plugin's usage manifest was truncated, so the findings are a floor.</summary>
	public bool UsageTruncated { get; init; }

	public required IReadOnlyList<CompatibilityFinding> Findings { get; init; }
}

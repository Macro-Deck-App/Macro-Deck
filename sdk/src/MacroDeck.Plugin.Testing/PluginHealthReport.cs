namespace MacroDeck.Plugin.Testing;

/// <summary>
/// One snapshot of a plugin's <c>/_macrodeck/*</c> endpoints, fetched together so a test can compare
/// liveness against readiness at the same instant - the two disagree only in the window before a
/// session exists, which is the case worth probing for on purpose.
/// </summary>
public sealed record PluginHealthReport
{
	/// <summary>True when <c>/_macrodeck/health</c> answered 200 - the process is serving, independent of any session.</summary>
	public required bool Live { get; init; }

	/// <summary>True when <c>/_macrodeck/ready</c> answered 200 - a session is open and usable.</summary>
	public required bool Ready { get; init; }

	/// <summary>Both <see cref="Live" /> and <see cref="Ready" />.</summary>
	public bool Healthy => Live && Ready;

	/// <summary>From <c>/_macrodeck/info</c>: the plugin's declared id.</summary>
	public string? Id { get; init; }

	/// <summary>From <c>/_macrodeck/info</c>: the plugin's declared name.</summary>
	public string? Name { get; init; }

	/// <summary>From <c>/_macrodeck/info</c>: the plugin's declared version.</summary>
	public string? Version { get; init; }

	/// <summary>From <c>/_macrodeck/info</c>: <c>"Managed"</c> or <c>"SelfRegistering"</c>.</summary>
	public string? Mode { get; init; }

	/// <summary>From <c>/_macrodeck/diagnostics</c>: the connection's status, e.g. <c>"Connected"</c>.</summary>
	public string? Status { get; init; }

	/// <summary>From <c>/_macrodeck/diagnostics</c>: the negotiated protocol version, once a session exists.</summary>
	public int? NegotiatedVersion { get; init; }

	/// <summary>From <c>/_macrodeck/diagnostics</c>: how many capabilities the plugin declared at handshake.</summary>
	public int? DeclaredCapabilities { get; init; }

	/// <summary>From <c>/_macrodeck/diagnostics</c>: how many of those the host accepted.</summary>
	public int? AcceptedCapabilities { get; init; }

	/// <summary>From <c>/_macrodeck/diagnostics</c>: how many capability invocations are running right now.</summary>
	public int? InFlightInvocations { get; init; }
}

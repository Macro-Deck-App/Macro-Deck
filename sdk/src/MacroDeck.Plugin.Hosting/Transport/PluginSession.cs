using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// One session as the client knows it: what the host issued, plus when, because the response does not
/// carry an issue time and the token's lifetime has to be measured from somewhere.
/// </summary>
internal sealed record PluginSession(
	string SessionId,
	string SessionToken,
	int NegotiatedVersion,
	PluginProtocolLimitsDescriptor Limits,
	PluginProtocolTimeoutsDescriptor Timeouts,
	DateTimeOffset OpenedAt)
{
	/// <summary>
	/// Whether a reconnect right now could still resume this session.
	///
	/// <para>
	/// The resume window is a minute and the session token lasts fifteen, so the window is always the
	/// binding constraint and the token needs no separate refresh: any reconnect too late to resume is
	/// already opening a new session, which issues a new token anyway.
	/// </para>
	/// </summary>
	public bool CanResumeAt(DateTimeOffset now, DateTimeOffset droppedAt)
		=> now - droppedAt < Timeouts.SessionResumeWindow;
}

/// <summary>Why a connection ended, and what the reconnect loop should do about it.</summary>
internal sealed record ConnectionOutcome(bool Fatal, string Reason, int? CloseCode = null)
{
	/// <summary>The socket dropped for a reason another attempt might not hit.</summary>
	public static ConnectionOutcome Retry(string reason, int? closeCode = null) => new(false, reason, closeCode);

	/// <summary>Retrying cannot help - the host said no in a way that will not change.</summary>
	public static ConnectionOutcome Fail(string reason, int? closeCode = null) => new(true, reason, closeCode);

	/// <summary>The plugin is shutting down.</summary>
	public static ConnectionOutcome Stopped { get; } = new(false, "The plugin is shutting down.");
}

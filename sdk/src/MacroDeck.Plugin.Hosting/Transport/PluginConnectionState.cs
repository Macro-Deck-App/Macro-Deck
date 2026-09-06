namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>Where the connection to the host currently is.</summary>
public enum PluginConnectionStatus
{
	/// <summary>Nothing has been attempted yet.</summary>
	Idle,

	/// <summary>Registering, opening a session, or connecting the socket.</summary>
	Connecting,

	/// <summary>A session is open and usable.</summary>
	Connected,

	/// <summary>The connection dropped and a retry is scheduled.</summary>
	Reconnecting,

	/// <summary>Something happened that retrying cannot fix. See <see cref="PluginConnectionState.FaultReason" />.</summary>
	Faulted,

	/// <summary>The plugin is shutting down or has shut down.</summary>
	Stopped
}

/// <summary>
/// The connection's observable state, so readiness and diagnostics can answer without reaching into
/// the transport. Written by the connection service, read by the endpoints - hence the volatile
/// reads rather than a lock: every field is a single value whose staleness by one update does not
/// matter to a diagnostic.
/// </summary>
public sealed class PluginConnectionState
{
	private int _inFlightInvocations;
	private int _reconnectAttempt;

	/// <summary>The current status.</summary>
	public PluginConnectionStatus Status { get; internal set; } = PluginConnectionStatus.Idle;

	/// <summary>Why the connection faulted, when it has.</summary>
	public string? FaultReason { get; internal set; }

	/// <summary>The open session's id, when there is one.</summary>
	public string? SessionId { get; internal set; }

	/// <summary>The protocol version in force for the open session.</summary>
	public int? NegotiatedVersion { get; internal set; }

	/// <summary>How many capabilities were declared when the session was opened.</summary>
	public int DeclaredCapabilities { get; internal set; }

	/// <summary>How many of those the host accepted.</summary>
	public int AcceptedCapabilities { get; internal set; }

	/// <summary>The close code of the last connection to end, when it ended with one.</summary>
	public int? LastCloseCode { get; internal set; }

	/// <summary>
	/// Which retry the connection is on. Zero while connected, and reset by every session that opens -
	/// so a plugin that reconnects after hours of healthy running retries from the initial delay rather
	/// than from where the last bout of trouble left off. This is the attempt number handed to
	/// <see cref="Protocol.Reconnection.ReconnectPolicy.DelayFor" />, not a separate tally of it: what a
	/// diagnostic reads here is the schedule the connection is actually on.
	/// </summary>
	public int ReconnectAttempt => Volatile.Read(ref _reconnectAttempt);

	/// <summary>How many invocations are running right now.</summary>
	public int InFlightInvocations => Volatile.Read(ref _inFlightInvocations);

	/// <summary>Whether the plugin is ready to serve, i.e. a session is open.</summary>
	public bool IsReady => Status == PluginConnectionStatus.Connected;

	/// <summary>
	/// The connection currently open to the host, or null between connections. <see cref="HostInvoker"/>
	/// sends through whichever connection is current rather than holding one itself, since a
	/// <see cref="PluginSessionConnection"/> is created fresh per connection attempt while the invoker
	/// is a singleton that outlives every one of them. Set by <see cref="PluginConnectionHostedService"/>;
	/// read on the send path, so it fails fast (no connection to send through) rather than buffering -
	/// the same behaviour <c>PluginSessionRegistry.SendToPlugin</c> gives the opposite direction.
	/// </summary>
	internal PluginSessionConnection? ActiveConnection { get; set; }

	/// <summary>
	/// Raised every time the handshake completes and the session becomes usable - including a resume.
	/// <see cref="Integrations.IntegrationLifecycleHostedService"/> is the subscriber: it initializes
	/// integrations on the first connect and re-initializes them on a reconnect that is not a resume,
	/// but does nothing on a resume, where the prior initialization still applies.
	/// </summary>
	public event EventHandler<PluginConnectedEventArgs>? Connected;

	internal void SetReconnectAttempt(int attempt) => Volatile.Write(ref _reconnectAttempt, attempt);

	internal void InvocationStarted() => Interlocked.Increment(ref _inFlightInvocations);

	internal void InvocationFinished() => Interlocked.Decrement(ref _inFlightInvocations);

	internal void RaiseConnected(bool resumed) => Connected?.Invoke(this, new PluginConnectedEventArgs(resumed));
}

/// <summary>Raised by <see cref="PluginConnectionState.Connected"/>.</summary>
public sealed class PluginConnectedEventArgs(bool resumed) : EventArgs
{
	/// <summary>True when this connect resumed the previous session rather than opening a new one.</summary>
	public bool Resumed { get; } = resumed;
}

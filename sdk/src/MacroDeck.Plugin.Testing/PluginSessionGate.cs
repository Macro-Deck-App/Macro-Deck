namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Gates <c>POST /api/plugins/sessions</c> on <see cref="MacroDeckTestHost" />.
///
/// <para>
/// A plugin's <c>/_macrodeck/health</c> is liveness: it answers as soon as the process serves,
/// independent of whether a session was ever opened - the supervisor depends on exactly that, since a
/// plugin whose health endpoint were gated on the connection would be indistinguishable from a hung
/// one. Proving that property from a test needs a host that can start serving without a session ever
/// completing, which a freshly started <see cref="MacroDeckTestHost" /> cannot do on its own: by
/// default it accepts the very first <c>POST /api/plugins/sessions</c> it receives, so a plugin
/// launched against it races its own handshake against the first health probe - a race a
/// <c>ProbeHealthAsync</c> that (incorrectly) reports readiness as merely "the process has not exited"
/// can win by accident as often as a correct implementation does.
/// </para>
///
/// <para>
/// Held with <see cref="Held" />, this gate removes the race instead of just narrowing it: every
/// session request is refused with a retryable error until <see cref="Release" /> runs, so a probe
/// taken while held is deterministically live but not ready.
/// </para>
/// </summary>
public sealed class PluginSessionGate
{
	private volatile bool _open;

	private PluginSessionGate(bool open)
	{
		_open = open;
	}

	/// <summary>
	/// A gate that lets every session request through immediately. The default for
	/// <see cref="MacroDeckTestHostOptions.SessionCreation" />, so a host behaves exactly as it did
	/// before this type existed unless a test opts into <see cref="Held" />.
	/// </summary>
	public static PluginSessionGate Open { get; } = new(open: true);

	/// <summary>
	/// Returns a new gate that refuses every <c>POST /api/plugins/sessions</c> request with a
	/// retryable error until <see cref="Release" /> is called - the plugin process still starts and
	/// serves its own <c>/_macrodeck/*</c> endpoints normally, only session creation is held. Unlike
	/// <see cref="Open" />, this is not a shared singleton: every call returns a fresh, independent
	/// instance, so one test releasing its gate can never affect another's.
	/// </summary>
	public static PluginSessionGate Held() => new(open: false);

	/// <summary>Whether a session request is currently let through.</summary>
	public bool IsOpen => _open;

	/// <summary>
	/// Opens the gate. A plugin that is already retrying its own session creation goes on to establish
	/// one on its next attempt - no restart needed. Idempotent, and safe to call even if the gate was
	/// never held.
	/// </summary>
	public void Release()
	{
		_open = true;
	}
}

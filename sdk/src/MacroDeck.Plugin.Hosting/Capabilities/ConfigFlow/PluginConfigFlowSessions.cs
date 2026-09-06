using System.Collections.Concurrent;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;

/// <summary>
/// The <c>sessionId -&gt; IConfigFlow</c> map a plugin host keeps across a config flow session's
/// lifetime. Shared as a singleton between <see cref="ConfigFlowCapabilityHandler" />, which owns
/// <c>flow.start</c>/<c>flow.submit</c>/<c>flow.abandon</c>, and
/// <see cref="MacroDeck.Plugin.Hosting.Capabilities.Ui.UiCapabilityHandler" />, which looks a flow up by
/// its session id to route an <c>integration-config</c> UI session to it - both handlers are constructed
/// once per plugin host, so registering this as a singleton is what lets them see the same instance.
/// </summary>
internal sealed class PluginConfigFlowSessions
{
	/// <summary>How long a session may sit untouched before it is swept - long enough to cover a user
	/// stepping away mid-OAuth-consent, short enough that an abandoned browser tab does not pin the
	/// plugin-side flow instance (and whatever it holds, e.g. an HTTP client mid-request) forever.</summary>
	internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

	public PluginConfigFlowSessions(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public bool TryGetFlow(string sessionId, out IConfigFlow flow)
	{
		if (_sessions.TryGetValue(sessionId, out var session))
		{
			flow = session.Flow;
			return true;
		}

		flow = null!;
		return false;
	}

	public void Touch(string sessionId)
	{
		if (_sessions.TryGetValue(sessionId, out var session))
		{
			session.LastTouchedUtc = _timeProvider.GetUtcNow();
		}
	}

	/// <summary>Stores a freshly started flow under its session id, returning whatever it replaced.
	/// Defensive: <c>flow.start</c> is only ever sent once per session id by <c>RemoteConfigFlow</c>, but
	/// a retry (or a misbehaving plugin host) must not leak the instance this replaces.</summary>
	public IConfigFlow? Set(string sessionId, IConfigFlow flow)
	{
		var replaced = _sessions.TryRemove(sessionId, out var previous) ? previous.Flow : null;
		_sessions[sessionId] = new Session(flow) { LastTouchedUtc = _timeProvider.GetUtcNow() };
		return replaced;
	}

	public bool TryRemove(string sessionId, out IConfigFlow flow)
	{
		if (_sessions.TryRemove(sessionId, out var session))
		{
			flow = session.Flow;
			return true;
		}

		flow = null!;
		return false;
	}

	/// <summary>Removes every session idle past <see cref="IdleTimeout" />, returning them for the caller
	/// to dispose. Checked at the top of every invocation rather than on a background timer, so a test
	/// can drive it deterministically with a fake <see cref="TimeProvider" /> instead of a real
	/// delay.</summary>
	public IReadOnlyList<IConfigFlow> SweepExpired()
	{
		var now = _timeProvider.GetUtcNow();
		List<IConfigFlow>? expired = null;

		foreach (var (sessionId, session) in _sessions)
		{
			if (now - session.LastTouchedUtc < IdleTimeout)
			{
				continue;
			}

			if (_sessions.TryRemove(sessionId, out var removed))
			{
				(expired ??= []).Add(removed.Flow);
			}
		}

		return (IReadOnlyList<IConfigFlow>?)expired ?? [];
	}

	private sealed class Session(IConfigFlow flow)
	{
		public IConfigFlow Flow { get; } = flow;

		public DateTimeOffset LastTouchedUtc { get; set; }
	}
}

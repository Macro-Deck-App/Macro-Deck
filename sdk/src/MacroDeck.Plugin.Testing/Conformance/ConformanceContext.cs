using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// Everything a running check can see: the host and subject <see cref="ConformanceRunner" /> already
/// established, the session on it (when the subject has one), and the options the whole run was
/// configured with.
///
/// <para>
/// Built once by <see cref="ConformanceRunner" /> for one run against one <see cref="ConformanceSubject" />
/// and shared, read-only, across every check in that run. A check that needs a second, independently
/// configured host or plugin instance - a non-overlapping protocol version, a shortened resume window, a
/// session gate held from the start - builds one itself through <see cref="Subject" /> rather than
/// disturbing <see cref="Host" />, which every other check in the same run is also using, and is
/// responsible for disposing whatever it starts.
/// </para>
/// </summary>
public sealed class ConformanceContext
{
	internal ConformanceContext(
		ConformanceSubject subject,
		MacroDeckTestHost host,
		PluginUnderTest plugin,
		PluginSessionView? session,
		PluginManifest? manifest,
		ManualTimeProvider? clock,
		ConformanceOptions options)
	{
		Subject = subject;
		Host = host;
		Plugin = plugin;
		Session = session;
		Manifest = manifest;
		Clock = clock;
		Logs = host.Logs;
		Options = options;
	}

	/// <summary>
	/// The recipe this run's ambient <see cref="Plugin" /> was started from. A check that needs an
	/// independently configured host or plugin instance calls <see cref="ConformanceSubject.StartAsync" />
	/// on this against a host it built itself.
	/// </summary>
	public ConformanceSubject Subject { get; }

	/// <summary>The host this run's ambient session, if any, was established against.</summary>
	public MacroDeckTestHost Host { get; }

	/// <summary>The running subject.</summary>
	public PluginUnderTest Plugin { get; }

	/// <summary>
	/// The ambient session, once the subject completed its handshake - null only when the subject never
	/// managed to connect at all, in which case every check declaring <see cref="ConformancePrecondition.Session" />
	/// in <see cref="IConformanceCheck.Requires" /> is skipped before it can observe that.
	/// </summary>
	public PluginSessionView? Session { get; }

	/// <summary>
	/// The subject's manifest - set when <see cref="Subject" /> is a <see cref="ConformanceSubject.InProcess" />
	/// or a <see cref="ConformanceSubject.Artifact" />; null for an <see cref="ConformanceSubject.Executable" />,
	/// which has no manifest resolved for it.
	/// </summary>
	public PluginManifest? Manifest { get; }

	/// <summary>
	/// The clock driving the subject's <see cref="TimeProvider" /> - set only when <see cref="Subject" /> is
	/// an <see cref="ConformanceSubject.InProcess" /> subject; see <see cref="ManualTimeProvider" />'s own
	/// remarks on why an external process cannot be handed one.
	/// </summary>
	public ManualTimeProvider? Clock { get; }

	/// <summary>Everything the subject has logged so far. Equivalent to <see cref="Host" />'s own <see cref="MacroDeckTestHost.Logs" />.</summary>
	public PluginLogCollector Logs { get; }

	/// <summary>The options this run was configured with.</summary>
	public ConformanceOptions Options { get; }
}

using System.ComponentModel.DataAnnotations;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// Everything about talking to the host, bound from the <c>MacroDeck:Plugin</c> configuration section
/// and therefore settable from <c>appsettings.json</c>, the environment or the command line. See
/// <c>PluginEnvironmentConfigurationSource</c> for the environment variable names.
/// </summary>
public sealed class PluginHostOptions
{
	/// <summary>The configuration section these options bind to.</summary>
	public const string SectionName = "MacroDeck:Plugin";

	/// <summary>Base address of the Macro Deck host, e.g. <c>http://127.0.0.1:8193</c>.</summary>
	[Required]
	public string HostUrl { get; set; } = "http://127.0.0.1:8193";

	/// <summary>Explicit mode. Left null, the mode is inferred from whether an id and secret are present.</summary>
	public PluginRegistrationMode? Mode { get; set; }

	/// <summary>Overrides the id declared with <c>WithId</c>. Set by the supervisor in managed mode.</summary>
	public string? Id { get; set; }

	/// <summary>The plugin secret. Injected by the supervisor in managed mode; persisted by the plugin otherwise.</summary>
	public string? Secret { get; set; }

	/// <summary>One-time token authorizing self-registration, obtained by the user from the host.</summary>
	public string? EnrollmentToken { get; set; }

	/// <summary>
	/// Identifies this process instance, so the host can tell a fresh connection from a resume under
	/// <c>MaxSessionsPerPlugin = 1</c>. Defaults to a new value per process.
	/// </summary>
	public string? InstanceId { get; set; }

	/// <summary>
	/// The launch this managed plugin was started under. Set by the host supervisor, never by the
	/// plugin itself; diagnostic and log-correlation only, never asserted on the wire.
	/// </summary>
	public string? LaunchId { get; set; }

	/// <summary>Where a self-registering plugin persists its credentials. Defaults per platform.</summary>
	public string? StateDirectory { get; set; }

	/// <summary>
	/// Where an installed plugin may keep its own state. Set by the host supervisor for a managed
	/// plugin, and null when the plugin was not launched by one.
	///
	/// <para>
	/// The host guarantees this directory outlives every update and rollback, because it sits beside the
	/// immutable per-version directories rather than inside one. Anything written next to the executable
	/// is lost the moment a new version is activated, so this is where settings, caches and databases
	/// belong. It is not the same as <see cref="StateDirectory"/>, which holds the SDK's own credentials
	/// for a self-registering plugin.
	/// </para>
	/// </summary>
	public string? DataDirectory { get; set; }

	/// <summary>
	/// Whether a failure to reach the host during startup should stop the application instead of
	/// retrying. Off by default: a plugin that is started before the host, or while the host is
	/// restarting, should wait rather than die.
	/// </summary>
	public bool FailFastOnFirstConnect { get; set; }

	/// <summary>
	/// Whether a protocol-fatal condition - an unsupported version, a session replaced by another
	/// instance, repeated authentication failures - stops the application. Defaults to on in managed
	/// mode, where the supervisor owns restarts, and off when self-registering, where the process may
	/// be doing other work and a loud log line is the better outcome.
	/// </summary>
	public bool? StopApplicationOnFatalProtocolError { get; set; }

	/// <summary>
	/// How many consecutive authentication failures are tolerated before the connection gives up. The
	/// persisted secret is never discarded automatically: silently throwing away a credential because
	/// the host had a bad minute is worse than failing loudly.
	/// </summary>
	[Range(1, 100)]
	public int MaxAuthenticationFailures { get; set; } = 3;

	/// <summary>
	/// Whether a self-registering plugin with no stored credential may fall back to interactive
	/// pairing - a prompt a human approves or rejects in the Macro Deck desktop app - instead of
	/// requiring <see cref="EnrollmentToken" />. Needs Developer Mode enabled on the host. An explicitly
	/// supplied <see cref="EnrollmentToken" /> always takes precedence over pairing.
	/// </summary>
	public bool PairingEnabled { get; set; } = true;

	/// <summary>
	/// How long to wait for a pairing request to be approved before giving up. Null uses the request
	/// lifetime the host itself advertises.
	/// </summary>
	public TimeSpan? PairingTimeout { get; set; }

	/// <summary>
	/// The process id of the host that launched this plugin, as an invariant-culture integer. Set by
	/// the host supervisor for a managed plugin; read as a raw string and parsed defensively, so a
	/// missing or malformed value only ever disables the host-liveness watch, never crashes it.
	/// </summary>
	public string? HostProcessId { get; set; }

	/// <summary>
	/// The start time of the host process named by <see cref="HostProcessId" />, in UTC round-trip
	/// ("O") format. Needed alongside the pid because process ids are reused: comparing both is what
	/// tells the original host apart from an unrelated process that later inherited the same id.
	/// </summary>
	public string? HostStartedAt { get; set; }

	/// <summary>
	/// Whether this plugin stops itself when the host process named by <see cref="HostProcessId" /> and
	/// <see cref="HostStartedAt" /> is gone, polling roughly every 5 seconds. Null (the default) watches
	/// only in <see cref="PluginRegistrationMode.Managed" /> mode, where the host launched the process
	/// and its death otherwise leaves the plugin orphaned forever; a self-registering plugin is an
	/// independent process the host did not launch and is left running by default. Set explicitly to
	/// force the watch on or off regardless of mode.
	/// </summary>
	public bool? ExitWhenHostProcessDies { get; set; }
}

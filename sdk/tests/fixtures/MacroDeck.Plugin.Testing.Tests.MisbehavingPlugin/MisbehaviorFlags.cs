namespace MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin;

/// <summary>
/// The tokens <c>MACRODECK_MISBEHAVE</c> (a comma-separated list) understands. Each names one
/// deliberately non-conforming behaviour this fixture can switch on, so a single built executable
/// serves every scenario that needs a plugin genuinely misbehaving in a real process rather than one
/// simulated in-process.
///
/// <para>
/// Public (unlike the rest of this fixture) so a consumer setting <c>MACRODECK_MISBEHAVE</c> - the
/// conformance suite's own counterexample tests, in particular - names a behaviour through this constant
/// rather than a hand-typed string that could drift from what <c>Program.cs</c> actually understands.
/// </para>
/// </summary>
public static class MisbehaviorFlags
{
	/// <summary>Writes a line to stderr and exits with code 3 before building a plugin at all.</summary>
	public const string ExitImmediately = "exit-immediately";

	/// <summary>Never returns from the integration's <c>ShutdownAsync</c>.</summary>
	public const string HangInStop = "hang-stop";

	/// <summary>The <c>stall</c> action awaits forever without observing its cancellation token.</summary>
	public const string IgnoreCancellation = "ignore-cancellation";

	/// <summary>Throws on the second <c>InitializeAsync</c> call instead of tolerating a reconnect re-initialization.</summary>
	public const string NonIdempotentInit = "non-idempotent-init";

	/// <summary>Declares an action whose description alone exceeds the protocol's message size limit.</summary>
	public const string OversizeDescribe = "oversize-describe";

	/// <summary>Declares two weather station instances sharing the same instance id.</summary>
	public const string DuplicateInstanceIds = "duplicate-instance-ids";

	/// <summary>Overwrites <c>ASPNETCORE_URLS</c> before the host is built, so it listens somewhere its launcher never probes.</summary>
	public const string SelfSetUrls = "self-set-urls";

	/// <summary>Declares a single weather station instance whose id fails <c>LocalIdKind.Resource</c> validation.</summary>
	public const string InvalidInstanceId = "invalid-instance-id";

	/// <summary>Declares two provided variables sharing the same <c>DefinitionId</c>.</summary>
	public const string DuplicateVariableDefinitionIds = "duplicate-variable-definition-ids";

	/// <summary>The <c>log-flood</c> action's final error line is written straight to stderr instead of
	/// through <c>ILogger</c>, so it never reaches <c>log.publish</c> and the host's log collector never sees it.</summary>
	public const string BypassLoggingPipeline = "bypass-logging-pipeline";

	/// <summary>The <c>report-state</c> action's snapshot reports two states sharing the same id.</summary>
	public const string DuplicateStateIds = "duplicate-state-ids";

	/// <summary>The <c>report-state</c> action's snapshot reports a state whose id fails <c>LocalIdKind.Declared</c> validation.</summary>
	public const string InvalidStateId = "invalid-state-id";

	/// <summary>The <c>report-icon</c> action's snapshot sets <c>NoIcon</c> while also naming a <c>Reference</c>
	/// and a non-empty <c>Version</c>, violating MDC0312's internal-consistency rule.</summary>
	public const string InconsistentIconSnapshot = "inconsistent-icon-snapshot";

	/// <summary>The <c>report-icon</c> action's <c>icon.content</c> reply claims a non-image media type for
	/// its bytes, violating MDC0313's own rule.</summary>
	public const string NonImageIconMediaType = "non-image-icon-media-type";

	/// <summary>Declares a variable carrying a write capability whose <c>SetValueAsync</c> then refuses
	/// every write with <c>NotWritable</c>, violating MDC0314's own rule.</summary>
	public const string WritableVariableRefusesWrite = "writable-variable-refuses-write";
}

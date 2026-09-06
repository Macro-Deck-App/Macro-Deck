namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// What a plugin reports about the SDK it was built against, sent on
/// <c>POST /api/plugins/sessions</c>. Optional and additive: a plugin that predates this sends nothing
/// and still negotiates a session normally.
///
/// <para>
/// The whole point of this block is to let the host distinguish two states it otherwise cannot: a plugin
/// that <em>is</em> calling a deprecated API, and one that merely <em>was built against</em> an SDK in
/// which something is deprecated. Issue #418 requires that the second is never reported as the first.
/// </para>
/// </summary>
public sealed record PluginSdkUsage
{
	/// <summary>The version of <c>MacroDeck.Sdk</c> the plugin was compiled against.</summary>
	public required string SdkVersion { get; init; }

	/// <summary>
	/// Documentation comment ids of the deprecated APIs the plugin actually references, captured at build
	/// time by the SDK's usage-manifest generator.
	///
	/// <para>
	/// <c>null</c> and empty mean different things and must never be conflated. <c>null</c> is "not
	/// reported" - the generator did not run, so the host can only infer from <see cref="SdkVersion" />.
	/// Empty is "reported, and none are used" - a positive statement the host can treat as confirmed.
	/// </para>
	/// </summary>
	public IReadOnlyList<string>? DeprecatedApis { get; init; }

	/// <summary>
	/// True when the generator hit its cap and dropped entries, so the host reports the list as a floor
	/// rather than as the complete set.
	/// </summary>
	public bool Truncated { get; init; }
}

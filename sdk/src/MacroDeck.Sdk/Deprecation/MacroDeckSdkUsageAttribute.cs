namespace MacroDeck.Sdk.Deprecation;

/// <summary>
/// The build-time record of which deprecated SDK APIs a plugin assembly actually uses, emitted onto the
/// assembly by <c>MacroDeck.Plugin.Analyzers</c>' usage-manifest generator and read back by
/// <c>MacroDeck.Plugin.Hosting</c> at startup.
///
/// <para>
/// This exists to separate two things the host otherwise cannot tell apart: a plugin that <em>is</em>
/// using a deprecated API, and a plugin that merely <em>was built against</em> an SDK version in which
/// some API is deprecated. Without it the host can only infer from a version number, and issue #418 is
/// explicit that an inference must never be presented as confirmed usage.
/// </para>
///
/// <para>
/// An assembly attribute rather than an embedded resource: the data comes from the compiler's semantic
/// model - the same analysis that raises MDP5002 - and a source generator cannot emit a resource, while
/// an MSBuild target that could has no semantic model and would be back to guessing from the SDK version.
/// Generating the attribute keeps the manifest and the warnings incapable of disagreeing.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MacroDeckSdkUsageAttribute : Attribute
{
	/// <param name="sdkVersion">See <see cref="SdkVersion" />.</param>
	/// <param name="deprecatedApis">See <see cref="DeprecatedApis" />.</param>
	public MacroDeckSdkUsageAttribute(string sdkVersion, string[] deprecatedApis)
	{
		SdkVersion = sdkVersion;
		DeprecatedApis = deprecatedApis;
	}

	/// <summary>The version of <c>MacroDeck.Sdk</c> the assembly was compiled against.</summary>
	public string SdkVersion { get; }

	/// <summary>
	/// The documentation comment ids of the deprecated APIs this assembly references.
	///
	/// <para>
	/// An <em>empty</em> array means the generator ran and found none - a positive statement the host
	/// treats as confirmed-clean. It is never null here; the null case is the absence of this attribute
	/// altogether, which is what an older plugin looks like.
	/// </para>
	/// </summary>
	public string[] DeprecatedApis { get; }

	/// <summary>
	/// Set when the generator hit its cap and dropped entries, so the host can say "at least these"
	/// rather than presenting a partial list as complete.
	/// </summary>
	public bool Truncated { get; set; }
}

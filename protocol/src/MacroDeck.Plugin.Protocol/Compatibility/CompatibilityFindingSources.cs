namespace MacroDeck.Plugin.Protocol.Compatibility;

/// <summary>
/// How the host came to know a compatibility finding - the evidence behind it, kept on the wire so the
/// UI can say so rather than presenting every finding as equally certain.
///
/// <para>
/// The distinction exists because the host has two very different kinds of knowledge about a plugin. It
/// knows exactly what was negotiated, and it knows exactly which deprecated APIs a plugin reported using
/// - but for a plugin that reports no usage manifest it can only reason from a version number. Issue
/// #418 requires those never to be presented alike.
/// </para>
///
/// <para>Append-only within a protocol major, like every other wire vocabulary here.</para>
/// </summary>
public static class CompatibilityFindingSources
{
	/// <summary>The plugin reported this exact API in its build-time usage manifest.</summary>
	public const string Confirmed = "confirmed";

	/// <summary>Observed directly in protocol or capability negotiation.</summary>
	public const string Negotiated = "negotiated";

	/// <summary>Derived from the reported SDK version alone - the plugin <em>may</em> be affected.</summary>
	public const string Inferred = "inferred";

	/// <summary>The plugin reported nothing the host could reason from.</summary>
	public const string Unknown = "unknown";

	public static readonly IReadOnlyList<string> All = [Confirmed, Negotiated, Inferred, Unknown];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? source) => source is not null && _known.Contains(source);
}

namespace MacroDeck.Sdk.Deprecation;

/// <summary>
/// Macro Deck's own deprecation metadata, applied <em>alongside</em> a standard
/// <see cref="ObsoleteAttribute" /> rather than instead of it.
///
/// <para>
/// <see cref="ObsoleteAttribute" /> stays because it is what every consumer already understands - the
/// compiler's CS0618, an IDE's strikethrough, third-party analyzers. What it cannot carry is the
/// lifecycle: <em>when</em> the API was deprecated, <em>which</em> release removes it, and what to do
/// instead. Those are exactly the fields the host reports at the handshake and the UI renders, so they
/// live here in structured form rather than being parsed back out of an English message.
/// </para>
///
/// <para>
/// The three fields a deprecation cannot be acted on without are constructor parameters rather than
/// initialized properties - an attribute's named arguments cannot be <c>required</c>, so making them
/// optional properties would let a half-declared deprecation compile. MDP5003 covers what the compiler
/// still cannot: a missing companion <c>[Obsolete]</c>, empty guidance, or a removal version that is not
/// after the deprecation version.
/// </para>
///
/// <para>
/// Using a member that carries this is reported as MDP5002 - or MDP5004 once <see cref="RemovedIn" />
/// has been reached, which means the member should already be gone. See
/// <c>docs/src/content/docs/policies/deprecations.md</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class |
	AttributeTargets.Interface |
	AttributeTargets.Struct |
	AttributeTargets.Enum |
	AttributeTargets.Delegate |
	AttributeTargets.Method |
	AttributeTargets.Constructor |
	AttributeTargets.Property |
	AttributeTargets.Event |
	AttributeTargets.Field,
	Inherited = false)]
public sealed class MacroDeckDeprecatedAttribute : Attribute
{
	/// <param name="deprecatedIn">See <see cref="DeprecatedIn" />.</param>
	/// <param name="removedIn">See <see cref="RemovedIn" />.</param>
	/// <param name="guidance">See <see cref="Guidance" />.</param>
	public MacroDeckDeprecatedAttribute(string deprecatedIn, string removedIn, string guidance)
	{
		DeprecatedIn = deprecatedIn;
		RemovedIn = removedIn;
		Guidance = guidance;
	}

	/// <summary>
	/// The version this API was deprecated in, as a plain <c>major.minor.patch</c> string. Never a range:
	/// a deprecation happens once, in one release.
	/// </summary>
	public string DeprecatedIn { get; }

	/// <summary>
	/// The version this API is planned to be removed in. A promise to plugin authors, and the input to
	/// MDP5004 and to the host's "update required" state - so it names a real future release, not
	/// "eventually".
	/// </summary>
	public string RemovedIn { get; }

	/// <summary>
	/// What to do instead, in one sentence. Required, because a deprecation warning a plugin author
	/// cannot act on is noise - see the "actionable migration guidance" requirement in issue #418.
	/// </summary>
	public string Guidance { get; }

	/// <summary>
	/// The replacement API, as a display name a human can search for (e.g.
	/// <c>MacroDeck.Sdk.Actions.IActionExecutor.ExecuteAsync</c>). Null when a deprecation has no
	/// one-for-one replacement and <see cref="Guidance" /> has to carry the whole story.
	/// </summary>
	public string? Replacement { get; set; }

	/// <summary>Optional deep link to longer migration notes.</summary>
	public string? MigrationUrl { get; set; }
}

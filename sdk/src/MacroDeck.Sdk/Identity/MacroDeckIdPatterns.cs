namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The identifier grammar as compile-time constants: the two regex patterns <see cref="MacroDeckId" />
/// compiles with <c>[GeneratedRegex]</c>, and the length limits it enforces alongside them.
///
/// <para>
/// Internal, not linked source's usual concern about accessibility - a build-time-only consumer,
/// <c>MacroDeck.Plugin.Analyzers</c>, links this file directly rather than referencing the assembly,
/// because <c>[GeneratedRegex]</c> is unavailable on the <c>netstandard2.0</c> target Roslyn analyzers
/// must build for. Linked source compiles into the analyzer regardless of accessibility, so keeping this
/// internal costs the analyzer nothing while avoiding growing a published SDK's public surface for a
/// build-time convenience. Change a value here and both <see cref="MacroDeckId" /> and the analyzer pick
/// it up - one source of truth, two compilations.
/// </para>
/// </summary>
internal static class MacroDeckIdPatterns
{
	public const int MaxOwnerIdLength = 128;

	public const int MaxDeclaredLocalIdLength = 64;

	public const int MaxResourceLocalIdLength = 256;

	// Anchored with \A and \z, not ^ and $: in .NET '$' also matches before a trailing newline, which
	// would let "play\n" pass as a declared id and then miss every lookup for "play".
	/// <summary>One lowercase-kebab segment: <c>time</c>, <c>music-player</c>, <c>obs2</c>.</summary>
	public const string SegmentPattern = @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z";

	/// <summary>Two or more of those segments joined by dots: <c>app.macro-deck.spotify</c>.</summary>
	public const string PackageOwnerPattern =
		@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*)+\z";
}

namespace MacroDeck.Sdk.Deprecation;

/// <summary>
/// One entry in <see cref="SdkDeprecations" />: everything the host needs to describe a deprecated API
/// to a user, without loading the plugin's own assemblies or resolving the symbol itself.
///
/// <para>
/// <see cref="ApiId" /> is Roslyn's documentation comment id (<c>M:MacroDeck.Sdk.Foo.Bar(System.String)</c>).
/// That form is used rather than a hand-written name because the analyzer, the usage-manifest generator
/// and this registry all have to agree on one spelling, and the compiler already computes it - any
/// hand-maintained alternative is a drift source.
/// </para>
/// </summary>
public sealed record SdkDeprecation
{
	public required string ApiId { get; init; }

	/// <summary>Human-readable form of <see cref="ApiId" />, for the UI.</summary>
	public required string DisplayName { get; init; }

	public required string DeprecatedIn { get; init; }

	public required string RemovedIn { get; init; }

	public required string Guidance { get; init; }

	public string? Replacement { get; init; }

	public string? MigrationUrl { get; init; }
}

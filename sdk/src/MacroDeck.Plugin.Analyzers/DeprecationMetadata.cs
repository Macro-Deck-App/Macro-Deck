using Microsoft.CodeAnalysis;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The values of one <c>[MacroDeckDeprecated]</c> application, read out of attribute metadata rather
/// than off the attribute type - an analyzer never references the SDK, it only ever sees whatever
/// compilation it is handed (see <see cref="WellKnownTypeNames" />).
///
/// <para>
/// Every field is nullable here even though the attribute declares them <c>required</c>: an author can
/// write a partially-applied attribute and this type has to describe that state so MDP5003 can report
/// it, rather than throwing while analyzing half-typed code in an IDE.
/// </para>
/// </summary>
internal readonly struct DeprecationMetadata
{
	private DeprecationMetadata(
		string? deprecatedIn,
		string? removedIn,
		string? guidance,
		string? replacement,
		string? migrationUrl)
	{
		DeprecatedIn = deprecatedIn;
		RemovedIn = removedIn;
		Guidance = guidance;
		Replacement = replacement;
		MigrationUrl = migrationUrl;
	}

	public string? DeprecatedIn { get; }

	public string? RemovedIn { get; }

	public string? Guidance { get; }

	public string? Replacement { get; }

	public string? MigrationUrl { get; }

	/// <summary>
	/// The one-line "what to do instead" appended to every MDP5002/MDP5004 message. Assembled here so
	/// the two rules cannot describe the same deprecation differently.
	/// </summary>
	public string DescribeGuidance()
	{
		var replacement = string.IsNullOrWhiteSpace(Replacement) ? null : $"Use '{Replacement}'.";
		var guidance = string.IsNullOrWhiteSpace(Guidance) ? null : Guidance;
		var url = string.IsNullOrWhiteSpace(MigrationUrl) ? null : $"See {MigrationUrl}.";

		var parts = new[] { replacement, guidance, url }.Where(part => part is not null);
		var joined = string.Join(" ", parts);

		return joined.Length == 0 ? "No migration guidance was declared for this API." : joined;
	}

	/// <summary>
	/// Reads the first <c>[MacroDeckDeprecated]</c> on <paramref name="symbol" />, if any. The three
	/// lifecycle values are constructor arguments and the two optional ones named arguments, mirroring
	/// the attribute's own shape; a positional slot can still be missing while an author is mid-edit,
	/// which is why every read is index-checked rather than assumed.
	/// </summary>
	public static bool TryRead(ISymbol symbol, out DeprecationMetadata metadata)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != WellKnownTypeNames.MacroDeckDeprecatedAttribute)
			{
				continue;
			}

			metadata = new DeprecationMetadata(Positional(attribute, 0),
				Positional(attribute, 1),
				Positional(attribute, 2),
				Named(attribute, "Replacement"),
				Named(attribute, "MigrationUrl"));

			return true;
		}

		metadata = default;
		return false;
	}

	/// <summary>
	/// True when <paramref name="version" /> is a well-formed version that is at or below
	/// <paramref name="ceiling" /> - the MDP5004 test, kept here so "past its removal version" means one
	/// thing. An unparseable version is never "past", because guessing would turn a typo into an error
	/// on an unrelated line.
	/// </summary>
	public static bool IsAtOrBelow(string? version, Version ceiling)
		=> Version.TryParse(version, out var parsed) && parsed <= ceiling;

	/// <summary>
	/// True when both versions parse and <paramref name="removedIn" /> is not strictly after
	/// <paramref name="deprecatedIn" /> - a lifecycle that removes an API before (or in) the release that
	/// deprecated it, which leaves plugin authors no version to migrate on.
	/// </summary>
	public static bool IsNotAfter(string? removedIn, string? deprecatedIn)
		=> Version.TryParse(removedIn, out var removed) &&
			Version.TryParse(deprecatedIn, out var deprecated) &&
			removed <= deprecated;

	private static string? Positional(AttributeData attribute, int index)
		=> attribute.ConstructorArguments.Length > index
			? attribute.ConstructorArguments[index].Value as string
			: null;

	private static string? Named(AttributeData attribute, string name)
	{
		foreach (var argument in attribute.NamedArguments)
		{
			if (argument.Key == name)
			{
				return argument.Value.Value as string;
			}
		}

		return null;
	}
}

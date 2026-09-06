using System.Diagnostics.CodeAnalysis;

namespace MacroDeck.Plugin.Packaging.Versioning;

/// <summary>
/// A deliberately small semver range grammar for plugin compatibility and dependency declarations:
/// <code>
/// range      := "*" | comparator ("," comparator)*
/// comparator := ("=" | "&gt;" | "&gt;=" | "&lt;" | "&lt;=")? version
/// </code>
/// A comma means AND, so <c>"&gt;=1.2.0,&lt;2.0.0"</c> is the idiomatic form. There is no caret, tilde,
/// <c>||</c> or wildcard: each of those means something subtly different in npm, NuGet and Cargo, and an
/// author who has to spell out two comparators cannot be wrong about what they wrote.
/// Precedence is plain SemVer 2.0, so <c>"&gt;=1.0.0"</c> is not satisfied by <c>1.0.0-beta.1</c>.
/// </summary>
public sealed class SemanticVersionRange
{
	private readonly IReadOnlyList<Comparator> _comparators;

	private SemanticVersionRange(IReadOnlyList<Comparator> comparators)
	{
		_comparators = comparators;
	}

	/// <summary>Matches every version, including prereleases. The parsed form of <c>"*"</c>.</summary>
	public static SemanticVersionRange Any { get; } = new([]);

	public static bool TryParse(string? value, [NotNullWhen(true)] out SemanticVersionRange? range)
	{
		range = null;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var trimmed = value.Trim();
		if (trimmed == "*")
		{
			range = Any;
			return true;
		}

		var comparators = new List<Comparator>();
		foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!TryParseComparator(part.Trim(), out var comparator))
			{
				return false;
			}

			comparators.Add(comparator);
		}

		if (comparators.Count == 0)
		{
			return false;
		}

		range = new SemanticVersionRange(comparators);
		return true;
	}

	/// <summary>True when <paramref name="version"/> satisfies every comparator. An empty comparator set
	/// (<c>"*"</c>) is satisfied by anything.</summary>
	public bool Satisfies(SemanticVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		return _comparators.All(comparator => comparator.Satisfies(version));
	}

	private static bool TryParseComparator(string value, out Comparator comparator)
	{
		comparator = default;

		var (op, versionText) = value switch
		{
			_ when value.StartsWith(">=", StringComparison.Ordinal) => (ComparatorOperator.GreaterOrEqual, value[2..]),
			_ when value.StartsWith("<=", StringComparison.Ordinal) => (ComparatorOperator.LessOrEqual, value[2..]),
			_ when value.StartsWith('>') => (ComparatorOperator.Greater, value[1..]),
			_ when value.StartsWith('<') => (ComparatorOperator.Less, value[1..]),
			_ when value.StartsWith('=') => (ComparatorOperator.Equal, value[1..]),
			_ => (ComparatorOperator.Equal, value)
		};

		if (!SemanticVersion.TryParse(versionText.Trim(), out var version))
		{
			return false;
		}

		comparator = new Comparator(op, version);
		return true;
	}

	private enum ComparatorOperator
	{
		Equal,
		Greater,
		GreaterOrEqual,
		Less,
		LessOrEqual
	}

	private readonly record struct Comparator(ComparatorOperator Operator, SemanticVersion Version)
	{
		public bool Satisfies(SemanticVersion candidate)
		{
			var comparison = candidate.CompareTo(Version);
			return Operator switch
			{
				ComparatorOperator.Equal => comparison == 0,
				ComparatorOperator.Greater => comparison > 0,
				ComparatorOperator.GreaterOrEqual => comparison >= 0,
				ComparatorOperator.Less => comparison < 0,
				ComparatorOperator.LessOrEqual => comparison <= 0,
				_ => false
			};
		}
	}
}

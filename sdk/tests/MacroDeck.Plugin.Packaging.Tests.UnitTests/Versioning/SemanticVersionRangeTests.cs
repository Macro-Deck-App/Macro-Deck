using MacroDeck.Plugin.Packaging.Versioning;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Versioning;

[TestFixture]
internal sealed class SemanticVersionRangeTests
{
	private static bool Satisfies(string range, string version)
	{
		Assert.That(SemanticVersionRange.TryParse(range, out var parsedRange),
			Is.True,
			$"'{range}' should parse.");
		Assert.That(SemanticVersion.TryParse(version, out var parsedVersion),
			Is.True,
			$"'{version}' should parse.");

		return parsedRange!.Satisfies(parsedVersion!);
	}

	[Test]
	public void A_bare_version_means_exact_equality_not_a_lower_bound()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies("1.2.3", "1.2.3"), Is.True);
			Assert.That(Satisfies("1.2.3", "1.2.4"), Is.False);
			Assert.That(Satisfies("1.2.3", "1.3.0"), Is.False);
			Assert.That(Satisfies("1.2.3", "2.0.0"), Is.False);
			Assert.That(Satisfies("1.2.3", "1.2.2"), Is.False);
		});
	}

	[Test]
	public void An_explicit_equals_comparator_behaves_the_same_as_a_bare_version()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies("=1.2.3", "1.2.3"), Is.True);
			Assert.That(Satisfies("=1.2.3", "1.2.4"), Is.False);
		});
	}

	[Test]
	public void A_comma_joins_comparators_with_and()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies(">=1.2.0,<2.0.0", "1.2.0"), Is.True);
			Assert.That(Satisfies(">=1.2.0,<2.0.0", "1.9.9"), Is.True);
			Assert.That(Satisfies(">=1.2.0,<2.0.0", "1.1.9"), Is.False);
			Assert.That(Satisfies(">=1.2.0,<2.0.0", "2.0.0"), Is.False);
		});
	}

	/// <summary>An or-joined implementation would satisfy this for everything, which no other test in
	/// this fixture would catch.</summary>
	[Test]
	public void An_unsatisfiable_conjunction_is_satisfied_by_nothing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies(">=2.0.0,<1.0.0", "1.0.0"), Is.False);
			Assert.That(Satisfies(">=2.0.0,<1.0.0", "1.5.0"), Is.False);
			Assert.That(Satisfies(">=2.0.0,<1.0.0", "2.0.0"), Is.False);
			Assert.That(Satisfies(">=2.0.0,<1.0.0", "3.0.0"), Is.False);
		});
	}

	[Test]
	public void A_prerelease_does_not_satisfy_a_stable_lower_bound_of_the_same_core()
	{
		Assert.That(Satisfies(">=1.0.0", "1.0.0-rc.1"), Is.False);
	}

	[Test]
	public void A_prerelease_lower_bound_admits_later_prereleases_and_the_release()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies(">=1.0.0-alpha", "1.0.0-alpha.1"), Is.True);
			Assert.That(Satisfies(">=1.0.0-alpha", "1.0.0-beta"), Is.True);
			Assert.That(Satisfies(">=1.0.0-alpha", "1.0.0"), Is.True);
		});
	}

	[Test]
	public void Build_metadata_does_not_affect_precedence()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies("=1.0.0", "1.0.0+build.5"), Is.True);
			Assert.That(Satisfies(">1.0.0+a", "1.0.0+b"), Is.False);
		});
	}

	/// <summary>Lexicographic comparison would rank "10" below "9" and get this backwards.</summary>
	[Test]
	public void Numeric_prerelease_identifiers_compare_numerically()
	{
		Assert.That(Satisfies(">1.0.0-alpha.9", "1.0.0-alpha.10"), Is.True);
	}

	[Test]
	public void A_wildcard_range_matches_every_version()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies("*", "0.0.1"), Is.True);
			Assert.That(Satisfies("*", "1.0.0"), Is.True);
			Assert.That(Satisfies("*", "99.0.0"), Is.True);
			Assert.That(Satisfies("*", "1.0.0-beta.1"), Is.True);
		});
	}

	/// <summary>
	/// The grammar is deliberately smaller than npm's or NuGet's. Delegating to either of those would
	/// silently accept these and change what every published dependency range means.
	/// </summary>
	[TestCase("^1.0.0")]
	[TestCase("~1.2.0")]
	[TestCase("1.x")]
	[TestCase("1.2.*")]
	[TestCase(">=1.0.0 || <2.0.0")]
	[TestCase("[1.0,2.0)")]
	[TestCase("1.2")]
	[TestCase("abc")]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase(null)]
	public void Unsupported_range_syntax_fails_to_parse(string? value)
	{
		Assert.Multiple(() =>
		{
			Assert.That(SemanticVersionRange.TryParse(value, out var range), Is.False);
			Assert.That(range, Is.Null);
		});
	}

	[Test]
	public void Whitespace_around_comparators_is_tolerated()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies(">= 1.0.0, < 2.0.0", "1.5.0"), Is.True);
			Assert.That(Satisfies(">= 1.0.0, < 2.0.0", "2.5.0"), Is.False);
		});
	}

	[Test]
	public void Every_comparator_operator_is_understood()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Satisfies(">1.0.0", "1.0.1"), Is.True);
			Assert.That(Satisfies(">1.0.0", "1.0.0"), Is.False);
			Assert.That(Satisfies("<=1.0.0", "1.0.0"), Is.True);
			Assert.That(Satisfies("<=1.0.0", "1.0.1"), Is.False);
			Assert.That(Satisfies("<1.0.0", "0.9.9"), Is.True);
		});
	}
}

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// Guards against a second, drifted implementation of the canonical plugin digest ever appearing
/// unnoticed: the literal <c>macro-deck-plugin/1</c> header must appear in exactly one non-test <c>.cs</c>
/// file in the whole repository - <c>PluginArtifactDigest.cs</c> itself.
/// </summary>
[TestFixture]
internal sealed class SingleDigestImplementationTests
{
	[Test]
	public void The_plugin_digest_header_literal_appears_in_exactly_one_non_test_source_file()
	{
		var repositoryRoot = FindRepositoryRoot();
		var matches = Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
				StringComparison.Ordinal))
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
				StringComparison.Ordinal))
			.Where(path => !HasTestsPathSegment(path))
			.Where(path => File.ReadAllText(path).Contains("macro-deck-plugin/1", StringComparison.Ordinal))
			.ToList();

		Assert.That(matches, Has.Count.EqualTo(1), $"Expected exactly one match, found: {string.Join(", ", matches)}");
		Assert.That(matches[0], Does.EndWith("PluginArtifactDigest.cs"));
	}

	/// <summary>True when a directory or dot-separated project-name segment of <paramref name="path"/>
	/// is exactly "Tests" - e.g. <c>sdk/tests/...</c> or the <c>Tests</c> component of
	/// <c>MacroDeck.Signing.Tests.UnitTests</c> - rather than merely containing "Tests" as a substring,
	/// which would also wrongly exclude a production path like <c>TestHarness/</c>.</summary>
	private static bool HasTestsPathSegment(string path) =>
		path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '.')
			.Any(segment => string.Equals(segment, "Tests", StringComparison.Ordinal));

	private static string FindRepositoryRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current is not null && !File.Exists(Path.Combine(current.FullName, "MacroDeck.slnx")))
		{
			current = current.Parent;
		}

		if (current is null)
		{
			throw new InvalidOperationException("Could not locate the repository root (MacroDeck.slnx) above " +
				AppContext.BaseDirectory);
		}

		return current.FullName;
	}
}

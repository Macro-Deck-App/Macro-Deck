using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

[TestFixture]
public class PluginRuntimeIdentifiersTests
{
	[TestCase("osx-arm64", "osx-x64")]
	[TestCase("win-arm64", "win-x64")]
	public void A_silicon_rid_falls_back_to_its_x64_counterpart(string rid, string fallback)
		=> Assert.That(PluginRuntimeIdentifiers.CandidatesFor(rid), Is.EqualTo(new[] { rid, fallback }));

	[TestCase("win-x64")]
	[TestCase("osx-x64")]
	[TestCase("linux-x64")]
	[TestCase("linux-arm64")]
	public void An_exact_match_rid_with_no_fallback_yields_only_itself(string rid)
		=> Assert.That(PluginRuntimeIdentifiers.CandidatesFor(rid), Is.EqualTo(new[] { rid }));

	[Test]
	public void An_unknown_rid_yields_only_itself()
	{
		var expected = new[] { "freebsd-x64" };

		Assert.That(PluginRuntimeIdentifiers.CandidatesFor("freebsd-x64"), Is.EqualTo(expected));
	}

	[Test]
	public void Linux_musl_resolves_no_usable_fallback()
	{
		// Explicit non-goal: musl gets no silicon-style fallback, so a manifest with no linux-musl-x64
		// entrypoint resolves nothing for it.
		var candidates = PluginRuntimeIdentifiers.CandidatesFor("linux-musl-x64");
		var expected = new[] { "linux-musl-x64" };

		Assert.That(candidates, Is.EqualTo(expected));
	}

	[Test]
	public void There_is_no_any_key_or_dotnet_muxer_fallback()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginRuntimeIdentifiers.CandidatesFor("win-x64"), Does.Not.Contain("any"));
			Assert.That(PluginRuntimeIdentifiers.CandidatesFor("win-x64"), Does.Not.Contain("dotnet"));
		});
	}
}

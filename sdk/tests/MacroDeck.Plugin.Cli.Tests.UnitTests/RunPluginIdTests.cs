using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// The managed-mode plugin id precedence issue #556's P1 finding is about:
/// <see cref="RunSession.ResolveManagedPluginId" /> together with <see cref="ManifestPeek" />, the two
/// pieces <c>run</c> combines to decide <c>MACRO_DECK_PLUGIN_ID</c> without ever spawning a process. This
/// is the exact equality <c>PluginHostBuilder.Build()</c> enforces against the manifest inside the child
/// process - get it wrong here and every default managed launch fails at startup, per the issue.
/// </summary>
[TestFixture]
public class RunPluginIdTests
{
	[Test]
	public void A_managed_run_takes_the_plugin_id_from_the_manifest_next_to_the_target()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();

		try
		{
			var peek = ManifestPeek.Read(directory);
			var resolved = RunSession.ResolveManagedPluginId(explicitId: null, peek.Id, useStub: true);

			// Compared against the fixture's own constant - never a value read back through the code under
			// test - so this can only pass if the manifest's id genuinely made it through unaltered.
			Assert.That(resolved, Is.EqualTo(ManifestFixtures.PluginId));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void An_explicit_plugin_id_overrides_the_manifest()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();

		try
		{
			var peek = ManifestPeek.Read(directory);
			var resolved = RunSession.ResolveManagedPluginId("com.explicit.override", peek.Id, useStub: true);

			Assert.That(resolved, Is.EqualTo("com.explicit.override"));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void Without_a_manifest_a_development_id_is_still_generated()
	{
		var first = RunSession.ResolveManagedPluginId(explicitId: null, manifestId: null, useStub: true);
		var second = RunSession.ResolveManagedPluginId(explicitId: null, manifestId: null, useStub: true);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.Null.And.Not.Empty);
			Assert.That(second, Is.Not.Null.And.Not.Empty);

			// Proves it is actually generated, rather than pinning the "dev.macrodeck-plugin." prefix -
			// that string is RunSession's own implementation detail, not part of the documented contract.
			Assert.That(first, Is.Not.EqualTo(second));
		});
	}

	[Test]
	public void The_manifest_peek_still_yields_the_graceful_timeout()
	{
		// Bounds taken from docs/src/content/docs/guides/packaging.md ("clamped 1-60s"), never from ManifestPeek's own
		// Math.Clamp call - this is a regression guard on the manifest read staying folded correctly into
		// the shared peek, not a restatement of the clamp's own arithmetic.
		var lowDirectory = ManifestFixtures.WriteManifestDirectoryWithGracePeriod(0);
		var highDirectory = ManifestFixtures.WriteManifestDirectoryWithGracePeriod(120);
		var inRangeDirectory = ManifestFixtures.WriteManifestDirectoryWithGracePeriod(30);

		try
		{
			var low = ManifestPeek.Read(lowDirectory);
			var high = ManifestPeek.Read(highDirectory);
			var inRange = ManifestPeek.Read(inRangeDirectory);

			Assert.Multiple(() =>
			{
				Assert.That(low.GracePeriod, Is.EqualTo(TimeSpan.FromSeconds(1)));
				Assert.That(high.GracePeriod, Is.EqualTo(TimeSpan.FromSeconds(60)));
				Assert.That(inRange.GracePeriod, Is.EqualTo(TimeSpan.FromSeconds(30)));
			});
		}
		finally
		{
			Directory.Delete(lowDirectory, recursive: true);
			Directory.Delete(highDirectory, recursive: true);
			Directory.Delete(inRangeDirectory, recursive: true);
		}
	}
}

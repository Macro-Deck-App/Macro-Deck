using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class WellBehavedPluginManifestTests
{
	[Test]
	public void The_fixtures_manifest_is_valid_and_its_icon_is_next_to_the_built_plugin()
	{
		var fixtureOutputDirectory = Path.GetDirectoryName(typeof(WellBehavedIntegration).Assembly.Location)!;
		var manifestPath = Path.Combine(fixtureOutputDirectory, "manifest.json");

		Assert.That(File.Exists(manifestPath), Is.True, $"Expected a manifest at '{manifestPath}'.");

		var result = new PluginManifestReader().Read(manifestPath, "app.macro-deck.well-behaved-test-plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Icon, Is.Not.Null.And.Not.Empty);

		var iconPath = Path.Combine(fixtureOutputDirectory,
			result.Manifest.Icon!.Replace('/', Path.DirectorySeparatorChar));

		Assert.That(File.Exists(iconPath), Is.True, $"Expected the declared icon at '{iconPath}'.");
	}
}

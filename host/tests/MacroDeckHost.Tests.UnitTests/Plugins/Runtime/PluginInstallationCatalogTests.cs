using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginInstallationCatalogTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private PluginInstallationCatalog CreateCatalog() => new(_paths, Serilog.Core.Logger.None);

	private void WritePlugin(string pluginId, IEnumerable<string> versions, string? currentVersion)
	{
		var pluginDirectory = Path.Combine(_paths.PluginsDirectory, pluginId);
		foreach (var version in versions)
		{
			var versionDirectory = Path.Combine(pluginDirectory, "versions", version);
			Directory.CreateDirectory(versionDirectory);
			File.WriteAllText(Path.Combine(versionDirectory, "manifest.json"), "{}");
		}

		if (currentVersion is not null)
		{
			Directory.CreateDirectory(pluginDirectory);
			File.WriteAllText(Path.Combine(pluginDirectory, "current.json"),
				$$"""{ "version": "{{currentVersion}}" }""");
		}
	}

	[Test]
	public void Multiple_versions_are_all_discovered_with_the_active_one_flagged()
	{
		WritePlugin("com.example.plugin", ["1.0.0", "1.1.0"], "1.1.0");

		var discovered = CreateCatalog().Discover();

		Assert.That(discovered, Has.Count.EqualTo(1));
		var plugin = discovered[0];
		Assert.Multiple(() =>
		{
			Assert.That(plugin.Versions, Has.Count.EqualTo(2));
			Assert.That(plugin.ActiveVersion!.Version, Is.EqualTo("1.1.0"));
		});
	}

	[Test]
	public void Current_json_naming_a_missing_version_has_no_active_version()
	{
		WritePlugin("com.example.plugin", ["1.0.0"], "9.9.9");

		var discovered = CreateCatalog().Discover();

		Assert.That(discovered[0].ActiveVersion, Is.Null);
	}

	[Test]
	public void A_malformed_current_json_does_not_throw_and_has_no_active_version()
	{
		var pluginDirectory = Path.Combine(_paths.PluginsDirectory, "com.example.plugin");
		Directory.CreateDirectory(Path.Combine(pluginDirectory, "versions", "1.0.0"));
		File.WriteAllText(Path.Combine(pluginDirectory, "versions", "1.0.0", "manifest.json"), "{}");
		File.WriteAllText(Path.Combine(pluginDirectory, "current.json"), "{ not json");

		IReadOnlyList<Application.Plugins.Runtime.InstalledPlugin>? discovered = null;
		Assert.DoesNotThrow(() => discovered = CreateCatalog().Discover());
		Assert.That(discovered![0].ActiveVersion, Is.Null);
	}

	[Test]
	public void A_directory_name_that_is_not_a_valid_plugin_id_is_skipped()
	{
		Directory.CreateDirectory(Path.Combine(_paths.PluginsDirectory, "Not A Valid Id!"));
		WritePlugin("com.example.plugin", ["1.0.0"], "1.0.0");

		var discovered = CreateCatalog().Discover();

		Assert.That(discovered.Select(p => p.PluginId).Single(), Is.EqualTo("com.example.plugin"));
	}

	[Test]
	public void A_missing_plugins_directory_discovers_nothing_and_does_not_throw()
	{
		IReadOnlyList<Application.Plugins.Runtime.InstalledPlugin>? discovered = null;
		Assert.DoesNotThrow(() => discovered = CreateCatalog().Discover());
		Assert.That(discovered, Is.Empty);
	}

	[Test]
	public void TryResolveActive_finds_the_active_version_of_an_installed_plugin()
	{
		WritePlugin("com.example.plugin", ["1.0.0"], "1.0.0");

		var found = CreateCatalog().TryResolveActive("com.example.plugin", out var version);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.True);
			Assert.That(version!.Version, Is.EqualTo("1.0.0"));
		});
	}
}

using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
internal sealed class WidgetProviderAvailabilityTests
{
	[Test]
	public void An_installed_plugin_uses_its_manifest_name_before_an_integration_registers()
	{
		const string pluginId = "com.example.installed-plugin";
		const string manifestPath = "plugin/versions/1.0.0/manifest.json";
		var installedVersion = new InstalledPluginVersion
		{
			Version = "1.0.0",
			VersionDirectory = "plugin/versions/1.0.0",
			ManifestPath = manifestPath
		};
		var catalog = new FakePluginInstallationCatalog();
		catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = "plugin",
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		});
		var manifests = new FakePluginManifestReader
		{
			Default = PluginManifestReadResult.Ok(new PluginManifest
			{
				ManifestVersion = PluginManifest.SupportedManifestVersion,
				Id = pluginId,
				Name = "Installed Plugin",
				Version = installedVersion.Version,
				Entrypoints = new Dictionary<string, PluginEntrypoint>()
			})
		};
		var availability = new WidgetProviderAvailability(
			new WidgetTypeRegistry(new RecordingMediator()),
			new StubIntegrationRegistry(),
			new NoPluginConnections(),
			catalog,
			manifests);

		var unavailable = availability.Check(pluginId + "::widget");

		Assert.Multiple(() =>
		{
			Assert.That(unavailable, Is.Not.Null);
			Assert.That(unavailable!.Name.Literal, Is.EqualTo("Installed Plugin"));
			Assert.That(manifests.Reads, Has.Count.EqualTo(1));
			Assert.That(manifests.Reads[0], Is.EqualTo((manifestPath, pluginId, installedVersion.Version)));
		});
	}
}

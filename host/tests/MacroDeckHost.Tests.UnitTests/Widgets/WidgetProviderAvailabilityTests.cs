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
	private const string PluginId = "com.example.installed-plugin";
	private const string WidgetType = PluginId + "::widget";

	private StubIntegrationRegistry _integrations = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private FakePluginManifestReader _manifests = null!;
	private WidgetProviderAvailability _availability = null!;

	[SetUp]
	public void SetUp()
	{
		_integrations = new StubIntegrationRegistry();
		_catalog = new FakePluginInstallationCatalog();
		_manifests = new FakePluginManifestReader();
		_availability = new WidgetProviderAvailability(new WidgetTypeRegistry(new RecordingMediator()),
			_integrations,
			new NoPluginConnections(),
			_catalog,
			_manifests);
	}

	[Test]
	public void An_installed_plugin_with_no_registered_integration_is_named_by_its_manifest()
	{
		Install("Installed Plugin");

		var unavailable = _availability.Check(WidgetType);

		Assert.That(unavailable?.Name.Literal, Is.EqualTo("Installed Plugin"));
	}

	[Test]
	public async Task A_stopped_plugin_without_a_readable_manifest_keeps_its_last_known_integration_name()
	{
		_integrations.Add(new FakeIntegration { Id = PluginId });
		_availability.Check(WidgetType);
		await _integrations.UnregisterAsync(PluginId);

		var unavailable = _availability.Check(WidgetType);

		Assert.That(unavailable?.Name.Literal, Is.EqualTo("Test Integration"));
	}

	[Test]
	public void A_plugin_with_no_known_name_is_named_by_its_id()
	{
		var unavailable = _availability.Check(WidgetType);

		Assert.That(unavailable?.Name.Literal, Is.EqualTo(PluginId));
	}

	private void Install(string name)
	{
		var version = new InstalledPluginVersion
		{
			Version = "1.0.0",
			VersionDirectory = "plugin/versions/1.0.0",
			ManifestPath = "plugin/versions/1.0.0/manifest.json"
		};
		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = PluginId,
			PluginDirectory = "plugin",
			Versions = [version],
			ActiveVersion = version
		});
		_manifests.Default = PluginManifestReadResult.Ok(new PluginManifest
		{
			ManifestVersion = PluginManifest.SupportedManifestVersion,
			Id = PluginId,
			Name = name,
			Version = version.Version,
			Entrypoints = new Dictionary<string, PluginEntrypoint>()
		});
	}
}

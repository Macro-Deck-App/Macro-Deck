using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>Update detection is notify-only: it must never start an install by itself, only report what
/// could be installed. These tests pin both halves of that contract - the comparison itself, and the
/// absence of any side effect on the operation tracker or the artifact downloader.</summary>
[TestFixture]
internal sealed class StoreUpdateDetectorTests
{
	private const string PluginId = "com.acme.hue";
	private const string IconPackId = "com.acme.material";

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;
	private PluginInstallationCatalog _plugins = null!;
	private StoreUpdateState _state = null!;
	private StoreUpdateDetector _detector = null!;
	private StoreOperationTracker _tracker = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_plugins = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_state = new StoreUpdateState();
		_detector = new StoreUpdateDetector(_catalog, _plugins, _installations, _state);
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private static StoreReleaseManifest Release(string version) => new()
	{
		Version = version,
		ArtifactUrl = new Uri($"https://cdn.example/artifact-{version}.bin"),
		Sha256 = new string('a', 64),
		Size = 16
	};

	[Test]
	public void An_out_of_date_plugin_and_icon_pack_are_reported_an_up_to_date_entry_is_not_and_no_operation_starts()
	{
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.Plugin,
					Id = PluginId,
					Name = "Hue Bridge",
					LatestVersion = "2.0.0",
					LatestRelease = Release("2.0.0")
				},
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.IconPack,
					Id = IconPackId,
					Name = "Material Icons",
					LatestVersion = "1.1.0",
					LatestRelease = Release("1.1.0")
				},
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.ProfileTemplate,
					Id = "com.acme.streamer",
					Name = "Streamer Profile",
					LatestVersion = "1.0.0",
					LatestRelease = Release("1.0.0")
				}
			]
		});

		InstallPluginVersion(PluginId, "1.0.0");
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow
		});
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.ProfileTemplate,
			PackageId = "com.acme.streamer",
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow
		});

		var updates = _detector.Check();

		Assert.Multiple(() =>
		{
			Assert.That(updates.Select(update => update.PackageId), Is.EquivalentTo(new[] { PluginId, IconPackId }));
			Assert.That(updates.Single(update => update.PackageId == PluginId).LatestVersion, Is.EqualTo("2.0.0"));
			Assert.That(updates.Single(update => update.PackageId == IconPackId).LatestVersion, Is.EqualTo("1.1.0"));
			Assert.That(_state.Current, Is.EquivalentTo(updates));

			// Notify-only: checking for updates must never itself start an install.
			Assert.That(_tracker.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public void A_removed_package_is_never_proposed_even_when_the_installed_version_is_out_of_date()
	{
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.IconPack,
					Id = IconPackId,
					Name = "Material Icons",
					LatestVersion = "2.0.0",
					LatestRelease = Release("2.0.0")
				}
			],
			RemovedPackages = [new StoreRemovedPackage { Id = IconPackId }]
		});

		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = IconPackId,
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow
		});

		var updates = _detector.Check();

		Assert.That(updates, Is.Empty);
	}

	private void InstallPluginVersion(string pluginId, string version)
	{
		var pluginDirectory = Path.Combine(_paths.PluginsDirectory, pluginId);
		var versionDirectory = Path.Combine(pluginDirectory, "versions", version);
		Directory.CreateDirectory(versionDirectory);
		File.WriteAllText(Path.Combine(versionDirectory, "manifest.json"), "{}");
		File.WriteAllText(Path.Combine(pluginDirectory, "current.json"),
			$"{{\"version\":\"{version}\"}}");
	}
}

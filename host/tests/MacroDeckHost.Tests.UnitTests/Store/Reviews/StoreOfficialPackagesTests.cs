using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StoreOfficialPackagesTests
{
	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Only_packages_recorded_from_the_official_registry_count_as_installed()
	{
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries = [Entry("com.acme.icons", StoreExtensionKind.IconPack), Entry("com.other.icons", StoreExtensionKind.IconPack)]
		});
		Record("com.acme.icons", StoreRegistryOptions.OfficialOrigin);
		Record("com.other.icons", "https://registry.example/");
		Record("com.acme.unlisted", StoreRegistryOptions.OfficialBaseUrl.ToString());

		var installed = Create(StoreRegistryOptions.Default).InstalledPackageIds();

		Assert.That(installed, Is.EquivalentTo(new[] { "com.acme.icons", "com.acme.unlisted" }));
	}

	[Test]
	public void A_store_plugin_removed_outside_the_store_no_longer_counts_as_installed()
	{
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [Entry("com.acme.hue", StoreExtensionKind.Plugin)] });
		_installations.Save(new StoreInstallationRecord
		{
			Origin = StoreRegistryOptions.OfficialOrigin,
			Kind = StoreExtensionKind.Plugin,
			PackageId = "com.acme.hue",
			Version = "1.0.0"
		});

		var installed = Create(StoreRegistryOptions.Default).InstalledPackageIds();

		Assert.That(installed, Is.Empty);
	}

	[Test]
	public void A_non_official_registry_offers_no_ratings_at_all()
	{
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [Entry("com.acme.icons", StoreExtensionKind.IconPack)] });
		Record("com.acme.icons", "https://registry.example/");
		var packages = Create(new StoreRegistryOptions { BaseUrl = new Uri("https://registry.example/") });

		Assert.Multiple(() =>
		{
			Assert.That(packages.ResolveListed(StoreExtensionKind.IconPack, "com.acme.icons"), Is.Null);
			Assert.That(packages.ListedPackageIds(["com.acme.icons"]), Is.Empty);
			Assert.That(packages.InstalledPackageIds(), Is.Empty);
		});
	}

	[Test]
	public void A_removed_package_is_no_longer_listed()
	{
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries = [Entry("com.acme.hue", StoreExtensionKind.Plugin), Entry("com.acme.gone", StoreExtensionKind.Plugin)],
			RemovedPackages = [new StoreRemovedPackage { Id = "com.acme.gone" }]
		});

		var listed = Create(StoreRegistryOptions.Default).ListedPackageIds(["COM.ACME.HUE", "com.acme.gone", "com.nope"]);

		Assert.That(listed, Is.EqualTo(new[] { "com.acme.hue" }));
	}

	[TestCase(BuildChannel.Development, "http://127.0.0.1:5199", "http://127.0.0.1:5199/")]
	[TestCase(BuildChannel.Development, "https://platform.test/base", "https://platform.test/base/")]
	[TestCase(BuildChannel.Development, "http://platform.test", "https://api.macro-deck.app/")]
	[TestCase(BuildChannel.Production, "http://127.0.0.1:5199", "https://api.macro-deck.app/")]
	[TestCase(BuildChannel.Development, null, "https://api.macro-deck.app/")]
	public void The_platform_address_can_only_be_redirected_by_a_development_build(BuildChannel channel,
		string? value,
		string expected) =>
		Assert.That(StorePlatformOptions.Resolve(channel, value).BaseUrl.ToString(), Is.EqualTo(expected));

	private StoreOfficialPackages Create(StoreRegistryOptions options) =>
		new(_catalog,
			new StoreCatalogQueryService(_catalog, new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None), _installations),
			_installations,
			options);

	private void Record(string packageId, string origin) =>
		_installations.Save(new StoreInstallationRecord
		{
			Origin = origin, Kind = StoreExtensionKind.IconPack, PackageId = packageId, Version = "1.0.0"
		});

	private static StoreCatalogEntry Entry(string id, StoreExtensionKind kind) => new()
	{
		Kind = kind,
		Id = id,
		Name = id,
		LatestVersion = "1.0.0",
		LatestRelease = new StoreReleaseManifest
		{
			Version = "1.0.0", ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"), Sha256 = new string('a', 64), Size = 16
		}
	};
}

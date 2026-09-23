using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreSimilarPackagesTests
{
	private static readonly string[] _unsupportedRids = ["not-a-real-rid"];

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private FakeStoreInstallCounts _installs = null!;
	private JsonStoreInstallationStore _installations = null!;
	private StoreSimilarPackages _similar = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);

		_catalog = new StoreCatalog();
		_installs = new FakeStoreInstallCounts();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_similar = new StoreSimilarPackages(new StoreCatalogQueryService(_catalog,
				new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None),
				_installations,
				new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None)),
			_installs);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task A_shared_tag_outranks_the_same_publisher_and_kind_even_with_more_installs()
	{
		Seed(Entry("origin", tags: ["obs", "streaming"], publisher: "Acme"),
			Entry("tagged-icons", StoreExtensionKind.IconPack, tags: ["streaming"], publisher: "Other"),
			Entry("same-publisher", tags: ["music"], publisher: "Acme"));
		_installs.Counts["same-publisher"] = 10_000;

		var ids = await Similar("origin");

		Assert.That(ids, Is.EqualTo(new[] { "tagged-icons", "same-publisher" }));
	}

	[Test]
	public async Task More_shared_tags_come_first_then_publisher_then_kind_then_installs()
	{
		Seed(Entry("origin", tags: ["obs", "streaming", "scenes"], publisher: "Acme"),
			Entry("one-tag", tags: ["obs"]),
			Entry("two-tags", tags: ["obs", "scenes"]),
			Entry("publisher-pack", StoreExtensionKind.IconPack, publisher: " acme "),
			Entry("kind-popular"),
			Entry("kind-quiet"));
		_installs.Counts["kind-popular"] = 50;

		var ids = await Similar("origin");

		Assert.That(ids, Is.EqualTo(new[] { "two-tags", "one-tag", "publisher-pack", "kind-popular", "kind-quiet" }));
	}

	[Test]
	public async Task Unrelated_installed_unsupported_and_the_package_itself_are_never_recommended()
	{
		Seed(Entry("origin", tags: ["obs"]),
			Entry("unrelated-icons", StoreExtensionKind.IconPack, tags: ["weather"]),
			Entry("unsupported", tags: ["obs"]) with { SupportedRids = _unsupportedRids },
			Entry("installed-icons", StoreExtensionKind.IconPack, tags: ["obs"]),
			Entry("fine", tags: ["obs"]));
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "official",
			Kind = StoreExtensionKind.IconPack,
			PackageId = "installed-icons",
			Version = "1.0.0"
		});

		var ids = await Similar("origin");

		Assert.That(ids, Is.EqualTo(new[] { "fine" }));
	}

	[Test]
	public async Task Two_packages_without_a_publisher_are_not_related_by_publisher()
	{
		Seed(Entry("origin", StoreExtensionKind.IconPack),
			Entry("anonymous-plugin"),
			Entry("blank-publisher-plugin", publisher: "  "));

		var ids = await Similar("origin", StoreExtensionKind.IconPack);

		Assert.That(ids, Is.Empty);
	}

	[Test]
	public async Task Without_install_data_equal_candidates_fall_back_to_name_order()
	{
		Seed(Entry("origin"), Entry("charlie"), Entry("alpha"), Entry("bravo"));
		_installs.Available = false;

		var ids = await Similar("origin");

		Assert.That(ids, Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
	}

	[Test]
	public async Task The_list_is_capped()
	{
		Seed([Entry("origin"), .. Enumerable.Range(0, 20).Select(index => Entry($"pkg{index:D2}"))]);

		var defaultList = await Similar("origin");
		var largest = await _similar.Find(StoreExtensionKind.Plugin, "origin", take: 500);

		Assert.Multiple(() =>
		{
			Assert.That(defaultList, Has.Count.EqualTo(StoreSimilarPackages.DefaultTake));
			Assert.That(largest.Data!, Has.Count.EqualTo(StoreSimilarPackages.MaxTake));
		});
	}

	[Test]
	public async Task An_unknown_package_is_reported_as_not_found()
	{
		Seed(Entry("origin"));

		var result = await _similar.Find(StoreExtensionKind.Plugin, "missing");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(StoreCatalogError.NotFound));
		});
	}

	private async Task<List<string>> Similar(string id, StoreExtensionKind kind = StoreExtensionKind.Plugin)
	{
		var result = await _similar.Find(kind, id);
		Assert.That(result.Success, Is.True);
		return result.Data!.Select(item => item.Entry.Id).ToList();
	}

	private void Seed(params StoreCatalogEntry[] entries) =>
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = entries.ToList() });

	private static StoreCatalogEntry Entry(string id,
		StoreExtensionKind kind = StoreExtensionKind.Plugin,
		string[]? tags = null,
		string? publisher = null) =>
		new()
		{
			Kind = kind,
			Id = id,
			Name = id,
			Publisher = publisher,
			Tags = tags ?? [],
			LatestVersion = "1.0.0",
			LatestRelease = new StoreReleaseManifest
			{
				Version = "1.0.0",
				ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"),
				Sha256 = new string('a', 64),
				Size = 16
			}
		};
}

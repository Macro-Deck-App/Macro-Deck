using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreLinkResolverTests
{
	private StoreCatalog _catalog = null!;

	[SetUp]
	public void SetUp() => _catalog = new StoreCatalog();

	[Test]
	public void A_listed_package_of_the_official_registry_resolves_to_its_own_kind()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin), Entry("com.acme.icons", StoreExtensionKind.IconPack));

		var resolution = Create().Resolve("com.acme.icons");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Outcome, Is.EqualTo(StoreLinkOutcome.Found));
			Assert.That(resolution.Kind, Is.EqualTo(StoreExtensionKind.IconPack));
			Assert.That(resolution.Id, Is.EqualTo("com.acme.icons"));
		});
	}

	[Test]
	public void A_package_the_registry_does_not_list_is_not_found()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin));

		Assert.That(Create().Resolve("com.acme.unlisted").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
	}

	[Test]
	public void A_withdrawn_package_is_not_found()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin), Entry("com.acme.gone", StoreExtensionKind.Plugin));
		_catalog.Swap(_catalog.Snapshot with
		{
			RemovedPackages = [new StoreRemovedPackage { Id = "com.acme.gone", Version = "1.0.0" }]
		});
		var resolver = Create();

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve("com.acme.gone").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
			Assert.That(resolver.Resolve("com.acme.hue").Outcome, Is.EqualTo(StoreLinkOutcome.Found));
		});
	}

	[Test]
	public void A_registry_other_than_the_official_one_never_resolves_anything()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin));
		var resolver = Create(new StoreRegistryOptions { BaseUrl = new Uri("https://registry.example/") });

		Assert.That(resolver.Resolve("com.acme.hue").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
	}

	[Test]
	public void An_id_that_two_kinds_share_is_not_resolved_because_the_address_names_no_kind()
	{
		Load(Entry("com.acme.both", StoreExtensionKind.Plugin), Entry("com.acme.both", StoreExtensionKind.IconPack));

		Assert.That(Create().Resolve("com.acme.both").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
	}

	[Test]
	public void A_registry_that_has_not_loaded_yet_is_reported_as_unavailable_rather_than_missing()
	{
		Assert.That(Create().Resolve("com.acme.hue").Outcome, Is.EqualTo(StoreLinkOutcome.RegistryUnavailable));
	}

	[Test]
	public void Only_an_exact_id_matches()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin));
		var resolver = Create();

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve("COM.ACME.HUE").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
			Assert.That(resolver.Resolve(" com.acme.hue").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
			Assert.That(resolver.Resolve("com.acme.hue/").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
			Assert.That(resolver.Resolve("com.acme.hue\n").Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("../../etc/passwd")]
	[TestCase("https://evil.example/registry")]
	[TestCase("com.acme.hue?registry=https://evil.example")]
	[TestCase("com.acme.hue;calc")]
	public void Input_that_is_not_a_package_id_is_not_found_without_error(string? hostile)
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin));

		Assert.That(Create().Resolve(hostile).Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
	}

	[Test]
	public void An_over_long_id_is_not_found()
	{
		Load(Entry("com.acme.hue", StoreExtensionKind.Plugin));

		Assert.That(Create().Resolve("com." + new string('a', 200)).Outcome, Is.EqualTo(StoreLinkOutcome.NotFound));
	}

	private StoreLinkResolver Create(StoreRegistryOptions? options = null) => new(_catalog, options ?? StoreRegistryOptions.Default);

	private void Load(params StoreCatalogEntry[] entries) =>
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = entries });

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

using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreCatalogQueryServiceTests
{
	private static readonly StoreExtensionKind[] _browseKinds =
		[StoreExtensionKind.Plugin, StoreExtensionKind.IconPack];

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private StoreCatalogQueryService _query = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);

		_catalog = new StoreCatalog();
		_query = new StoreCatalogQueryService(_catalog,
			new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None),
			new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None));
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void A_page_reports_how_many_entries_match_not_how_many_it_returns()
	{
		Seed(Enumerable.Range(1, 7).Select(number => Entry($"p{number}", $"Plugin {number}")));

		var first = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Take = 3 });
		var last = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Skip = 6, Take = 3 });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(first), Is.EqualTo(new[] { "p1", "p2", "p3" }));
			Assert.That(first.Total, Is.EqualTo(7));
			Assert.That(Ids(last), Is.EqualTo(new[] { "p7" }));
			Assert.That(last.Total, Is.EqualTo(7));
		});
	}

	[Test]
	public void The_total_counts_what_the_query_matches_not_the_whole_catalog()
	{
		Seed([
			Entry("com.acme.hue", "Hue Bridge"),
			Entry("com.acme.deck", "Deck Tools"),
			Entry("com.acme.hue-icons", "Hue Icons", StoreExtensionKind.IconPack)
		]);

		var page = Page(new StoreCatalogQuery { Kinds = [StoreExtensionKind.IconPack], Search = "hue" });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "com.acme.hue-icons" }));
			Assert.That(page.Total, Is.EqualTo(1));
		});
	}

	[Test]
	public void Paging_partitions_one_ordering_rather_than_sorting_each_page()
	{
		Seed([
			Entry("e", "Echo"), Entry("c", "Charlie"), Entry("a", "Alpha"), Entry("d", "Delta"), Entry("b", "Bravo")
		]);

		var pages = new[] { 0, 2, 4 }
			.Select(skip => Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Skip = skip, Take = 2 }))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(Ids(pages[0]), Is.EqualTo(new[] { "a", "b" }));
			Assert.That(Ids(pages[1]), Is.EqualTo(new[] { "c", "d" }));
			Assert.That(Ids(pages[2]), Is.EqualTo(new[] { "e" }));
		});
	}

	// Two entries sharing a timestamp is the ordinary case for a registry that publishes a batch, and
	// an ordering that leaves their relative place undecided hands one of them to two pages and the
	// other to none.
	[Test]
	public void Entries_that_tie_on_the_sort_key_keep_the_same_place_on_every_page()
	{
		var tie = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		// Seeded against the resolved order, so an implementation that leans on the sort being stable
		// keeps b ahead of a and fails: ties have to be settled, not inherited from the registry.
		Seed([
			Entry("b", "Bravo", createdAt: tie),
			Entry("a", "Alpha", createdAt: tie),
			Entry("c", "Charlie", createdAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)),
			Entry("d", "Delta", createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
		]);

		var whole = Ids(Page(new StoreCatalogQuery { Section = StoreCatalogSection.Newest }));
		var paged = Enumerable.Range(0, 4)
			.SelectMany(skip => Ids(Page(new StoreCatalogQuery
			{
				Section = StoreCatalogSection.Newest,
				Skip = skip,
				Take = 1
			})))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(whole, Is.EqualTo(new[] { "c", "a", "b", "d" }));
			Assert.That(paged, Is.EqualTo(new[] { "c", "a", "b", "d" }));
		});
	}

	[Test]
	public void Newest_orders_by_when_an_entry_arrived_and_recently_updated_by_when_it_changed()
	{
		var recent = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
		var old = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
		Seed([
			Entry("a", "Alpha", createdAt: recent, updatedAt: old),
			Entry("b", "Bravo", createdAt: old, updatedAt: recent)
		]);

		var newest = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Newest });
		var updated = Page(new StoreCatalogQuery { Section = StoreCatalogSection.RecentlyUpdated });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(newest), Is.EqualTo(new[] { "a", "b" }));
			Assert.That(Ids(updated), Is.EqualTo(new[] { "b", "a" }));
		});
	}

	[Test]
	public void An_entry_the_registry_gave_no_date_is_never_presented_as_the_newest_thing()
	{
		Seed([
			Entry("undated", "Undated"),
			Entry("new", "New", createdAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
			Entry("old", "Old", createdAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
		]);

		var page = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Newest });

		Assert.That(Ids(page), Is.EqualTo(new[] { "new", "old", "undated" }));
	}

	[Test]
	public void Browsing_without_a_search_term_is_name_order_however_the_registry_listed_it()
	{
		Seed([Entry("z", "zeta"), Entry("a", "Alpha"), Entry("m", "mango")]);

		var browsing = Page(new StoreCatalogQuery { Section = StoreCatalogSection.All });
		var byName = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(browsing), Is.EqualTo(new[] { "a", "m", "z" }));
			Assert.That(Ids(byName), Is.EqualTo(new[] { "a", "m", "z" }));
		});
	}

	[Test]
	public void An_explicitly_chosen_ordering_survives_a_search_term()
	{
		SeedSearchable();

		var byName = Page(new StoreCatalogQuery { Search = "hue", Section = StoreCatalogSection.Name });
		var byNewest = Page(new StoreCatalogQuery { Search = "hue", Section = StoreCatalogSection.Newest });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(byName), Is.EqualTo(new[] { "bright", "bridge", "zebra" }));
			Assert.That(Ids(byNewest), Is.EqualTo(new[] { "zebra", "bridge", "bright" }));
		});
	}

	[Test]
	public void A_search_without_a_chosen_ordering_puts_the_best_match_first()
	{
		SeedSearchable();

		var page = Page(new StoreCatalogQuery { Search = "hue", Section = StoreCatalogSection.All });

		Assert.That(Ids(page)[0], Is.EqualTo("bridge"));
	}

	[Test]
	public void A_query_for_plugins_and_icon_packs_returns_neither_another_kind_nor_fewer()
	{
		Seed([
			Entry("p1", "Plugin One"),
			Entry("p2", "Plugin Two"),
			Entry("i1", "Icons", StoreExtensionKind.IconPack),
			Entry("t1", "Template One", StoreExtensionKind.ProfileTemplate),
			Entry("t2", "Template Two", StoreExtensionKind.ProfileTemplate)
		]);

		var page = Page(new StoreCatalogQuery { Kinds = _browseKinds, Section = StoreCatalogSection.Name });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "i1", "p1", "p2" }));
			Assert.That(page.Total, Is.EqualTo(3));
			Assert.That(page.Items.Select(item => item.Entry.Kind),
				Has.None.EqualTo(StoreExtensionKind.ProfileTemplate));
		});
	}

	[Test]
	public void A_package_the_registry_withdrew_is_not_served_even_when_it_is_still_featured()
	{
		Seed([Entry("com.acme.hue", "Hue Bridge"), Entry("com.acme.gone", "Gone")],
			featured:
			[
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "com.acme.gone" },
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "com.acme.hue" }
			],
			removed: [new StoreRemovedPackage { Id = "com.acme.gone" }]);

		var featured = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Featured });
		var everything = Page(new StoreCatalogQuery { Section = StoreCatalogSection.All });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(featured), Is.EqualTo(new[] { "com.acme.hue" }));
			Assert.That(featured.Total, Is.EqualTo(1));
			Assert.That(Ids(everything), Is.EqualTo(new[] { "com.acme.hue" }));
		});
	}

	[Test]
	public void The_featured_section_keeps_the_order_the_registry_published()
	{
		Seed([Entry("z", "Zulu"), Entry("a", "Alpha"), Entry("m", "Mike")],
			featured:
			[
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "z" },
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "a" }
			]);

		var page = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Featured });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "z", "a" }));
			Assert.That(page.Total, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_featured_entry_the_catalog_cannot_resolve_is_skipped_and_the_rest_still_serve()
	{
		Seed([Entry("com.acme.hue", "Hue Bridge"), Entry("com.acme.material", "Material", StoreExtensionKind.IconPack)],
			featured:
			[
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "com.acme.ghost" },
				new StoreFeaturedRef { Kind = StoreExtensionKind.IconPack, Id = "com.acme.hue" },
				new StoreFeaturedRef { Kind = StoreExtensionKind.Plugin, Id = "com.acme.hue" }
			]);

		var page = Page(new StoreCatalogQuery { Kinds = _browseKinds, Section = StoreCatalogSection.Featured });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "com.acme.hue" }));
			Assert.That(page.Total, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_registry_that_features_nothing_answers_with_an_empty_section_not_a_failure()
	{
		Seed([Entry("p1", "Plugin One"), Entry("p2", "Plugin Two")]);

		var result = _query.Query(new StoreCatalogQuery { Section = StoreCatalogSection.Featured });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Items, Is.Empty);
			Assert.That(result.Data!.Total, Is.Zero);
		});
	}

	[Test]
	public void A_catalog_larger_than_one_page_is_reachable_a_page_at_a_time()
	{
		Seed(Enumerable.Range(100, 150).Select(number => Entry($"p{number}", $"Plugin {number}")));

		var first = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Take = 500 });
		var second = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Skip = 100, Take = 100 });
		var past = Page(new StoreCatalogQuery { Section = StoreCatalogSection.Name, Skip = 200, Take = 100 });

		Assert.Multiple(() =>
		{
			Assert.That(first.Items, Has.Count.EqualTo(StoreCatalogQuery.MaxTake));
			Assert.That(first.Total, Is.EqualTo(150));
			Assert.That(second.Items, Has.Count.EqualTo(50));
			Assert.That(second.Total, Is.EqualTo(150));
			Assert.That(Ids(first).Concat(Ids(second)).Distinct(), Has.Exactly(150).Items);
			Assert.That(past.Items, Is.Empty);
			Assert.That(past.Total, Is.EqualTo(150));
		});
	}

	[Test]
	public void An_uninitialised_registry_cannot_be_browsed_at_all()
	{
		var result = _query.Query(new StoreCatalogQuery());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(StoreCatalogError.RegistryUnavailable));
		});
	}

	private void SeedSearchable() =>
		Seed([
			Entry("bright", "Bright Hue", createdAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
			Entry("bridge", "Hue Bridge", createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
			Entry("zebra", "Zebra Hue Lights", createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
		]);

	private void Seed(IEnumerable<StoreCatalogEntry> entries,
		IEnumerable<StoreFeaturedRef>? featured = null,
		IEnumerable<StoreRemovedPackage>? removed = null) =>
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries = entries.ToList(),
			Featured = featured?.ToList() ?? [],
			RemovedPackages = removed?.ToList() ?? []
		});

	private StoreCatalogPage Page(StoreCatalogQuery query)
	{
		var result = _query.Query(query);
		Assert.That(result.Success, Is.True);
		return result.Data!;
	}

	private static List<string> Ids(StoreCatalogPage page) =>
		page.Items.Select(item => item.Entry.Id).ToList();

	private static StoreCatalogEntry Entry(string id,
		string name,
		StoreExtensionKind kind = StoreExtensionKind.Plugin,
		DateTimeOffset? createdAt = null,
		DateTimeOffset? updatedAt = null) =>
		new()
		{
			Kind = kind,
			Id = id,
			Name = name,
			LatestVersion = "1.0.0",
			CreatedAt = createdAt,
			UpdatedAt = updatedAt,
			LatestRelease = new StoreReleaseManifest
			{
				Version = "1.0.0",
				ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"),
				Sha256 = new string('a', 64),
				Size = 16
			}
		};
}

using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>The official registry must fail closed: anything the signed manifest does not vouch for is
/// refused, and a refusal never costs the catalog that was already verified.</summary>
[TestFixture]
internal sealed class StoreRegistryRefresherTests
{
	private static readonly string[] _registryUsage = [SigningCertificateChain.RegistryKeyUsage];
	private static readonly string[] _packageUsage = [SigningCertificateChain.PackageKeyUsage];
	private static readonly string[] _expectedHistory = ["2.2.0", "2.1.0", "2.0.0"];

	private static readonly string[] _expectedLanguages = ["en", "de", "zh-Hant-TW"];
	private static readonly string[] _seededIds = ["com.acme.hue", "com.acme.material", "com.acme.streamer"];

	private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

	private TestPaths _paths = null!;
	private ManualTimeProvider _time = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreRegistryStateStore _state = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreRegistryDirectory);
		Directory.CreateDirectory(_paths.StoreRegistryStagingDirectory);
		_time = new ManualTimeProvider { Now = _now };
		_catalog = new StoreCatalog();
		_state = new JsonStoreRegistryStateStore(_paths, Log.Logger);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private StoreRegistryRefresher Create(StoreRegistryFixture fixture, TimeSpan? maxSnapshotAge = null) =>
		new(fixture.Build(),
			StoreRegistryOptions.Default with
			{
				BaseUrl = new Uri(StoreRegistryFixture.BaseUrl),
				RootPublicKeyOverride = TestPki.Root.PublicKey,
				MaxSnapshotAge = maxSnapshotAge ?? StoreRegistryOptions.Default.MaxSnapshotAge
			},
			_state,
			new StoreRegistryReader(Log.Logger),
			_catalog,
			_paths,
			_time,
			Log.Logger);

	private static StoreRegistryFixture Registry(long sequence = 1,
		TestPki.IssuedCertificate? certificate = null)
	{
		var fixture = new StoreRegistryFixture(certificate) { Sequence = sequence };
		fixture.SignedAt = _now;
		fixture.AddPackage("plugin",
			"com.acme.hue",
			"2.1.0",
			name: "Hue Bridge",
			description: "Philips Hue lights",
			publisher: "Acme");
		fixture.AddPackage("icon-pack",
			"com.acme.material",
			"1.0.0",
			name: "Material Icons",
			publisher: "Acme");
		fixture.AddPackage("profile",
			"com.acme.streamer",
			"1.0.0",
			name: "Streamer Profile",
			publisher: "Acme");
		return fixture;
	}

	[Test]
	public async Task A_signed_registry_is_accepted_and_its_packages_become_the_catalog()
	{
		var refresher = Create(Registry());

		var result = await refresher.Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id),
				Is.EquivalentTo(_seededIds));
			Assert.That(refresher.Status.HasCatalog, Is.True);
		});
	}

	[Test]
	public async Task Every_published_version_contributes_its_own_release_notes_newest_first()
	{
		var fixture = Registry();
		fixture.AddVersion("plugins", "com.acme.hue", "2.0.0");
		fixture.AddVersion("plugins", "com.acme.hue", "2.2.0");

		await Create(fixture).Refresh();

		var history = _catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.hue").History;
		Assert.Multiple(() =>
		{
			Assert.That(history.Select(entry => entry.Version), Is.EqualTo(_expectedHistory));
			Assert.That(history[0].Changelog, Is.EqualTo("# 2.2.0"));
		});
	}

	/// <summary>The registry is the only thing that can say which languages a package ships before it is
	/// installed; a package that declares none says nothing, which is never the same as "English only".</summary>
	[Test]
	public async Task A_packages_declared_languages_reach_the_catalog_and_an_absent_list_stays_empty()
	{
		var fixture = Registry();
		fixture.AddPackage("plugin",
			"com.acme.translated",
			"1.0.0",
			name: "Translated Plugin",
			languages: ["en", "de", "zh-Hant-TW"]);

		await Create(fixture).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(_catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.translated").Languages,
				Is.EqualTo(_expectedLanguages));
			Assert.That(_catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.hue").Languages,
				Is.Empty);
		});
	}

	[Test]
	public async Task A_published_featured_list_reaches_the_catalog_in_the_order_it_was_published()
	{
		var fixture = Registry();
		fixture.Featured =
		[
			new { kind = "icon-pack", id = "com.acme.material" },
			new { kind = "plugin", id = "com.acme.hue" }
		];

		var result = await Create(fixture).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_catalog.Snapshot.Featured.Select(reference => (reference.Kind, reference.Id)),
				Is.EqualTo(new[]
				{
					(StoreExtensionKind.IconPack, "com.acme.material"),
					(StoreExtensionKind.Plugin, "com.acme.hue")
				}));
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Is.EquivalentTo(_seededIds));
		});
	}

	[Test]
	public async Task A_registry_that_publishes_no_featured_list_refreshes_and_features_nothing()
	{
		var result = await Create(Registry()).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Is.EquivalentTo(_seededIds));
			Assert.That(_catalog.Snapshot.Featured, Is.Empty);
		});
	}

	// The two lists fail differently on purpose: an index entry the tree cannot describe means the
	// snapshot would be incomplete, while a curated pick that resolves to nothing costs a tile - and
	// refusing the refresh over one would also stop the removal and key-revocation news it carries.
	[Test]
	public async Task A_featured_pick_naming_no_package_still_refreshes_where_an_index_entry_would_not()
	{
		var featuresAGhost = Registry();
		featuresAGhost.Featured = [new { kind = "plugin", id = "com.acme.ghost" }];

		var featuredResult = await Create(featuresAGhost).Refresh();

		var listsAGhost = Registry(sequence: 2);
		listsAGhost.PluginIds.Add("com.acme.ghost");

		var indexResult = await Create(listsAGhost).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(featuredResult.Success, Is.True);
			Assert.That(indexResult.Success, Is.False);
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Is.EquivalentTo(_seededIds));
		});
	}

	[Test]
	public async Task A_lower_sequence_is_refused_and_the_verified_catalog_is_kept()
	{
		var certificate = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: _registryUsage);
		var refresher = Create(Registry(sequence: 7, certificate: certificate));
		await refresher.Refresh();

		// Internally perfect, only older: nothing but the sequence check can reject this.
		var rolledBack = Registry(sequence: 6, certificate: certificate);
		rolledBack.PluginIds.Clear();
		rolledBack.IconPackIds.Clear();
		rolledBack.ProfileTemplateIds.Clear();
		rolledBack.AddPackage("plugin", "com.acme.hue", "2.0.0");
		var rollbackRefresher = Create(rolledBack);

		var result = await rollbackRefresher.Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.SequenceRollback));
			Assert.That(_catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.hue").LatestVersion,
				Is.EqualTo("2.1.0"));
			Assert.That(_state.Load(StoreRegistryFixture.BaseUrl)?.AcceptedSequence, Is.EqualTo(7));
		});
	}

	[Test]
	public async Task A_file_whose_bytes_do_not_match_the_declared_digest_is_refused()
	{
		var fixture = Registry();
		fixture.DeclaredDigestOverrides["index.json"] = new string('b', 64);

		var result = await Create(fixture).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_catalog.Snapshot.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_file_served_at_a_size_the_manifest_does_not_declare_is_refused()
	{
		var fixture = Registry();
		fixture.DeclaredSizeOverrides["index.json"] = 4;

		var result = await Create(fixture).Refresh();

		Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.SizeMismatch));
	}

	[Test]
	public async Task A_registry_file_the_manifest_does_not_list_is_refused()
	{
		var fixture = Registry();
		fixture.Unlisted.Add("security.json");

		var result = await Create(fixture).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_catalog.Snapshot.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_duplicated_manifest_entry_is_refused()
	{
		var fixture = Registry();
		fixture.DuplicateEntries.Add("index.json");

		var result = await Create(fixture).Refresh();

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public async Task A_package_usage_certificate_cannot_sign_the_registry()
	{
		var packageCertificate = TestPki.IssueCertificate(subjectKind: "creator",
			keyUsage: _packageUsage);

		var result = await Create(Registry(certificate: packageCertificate)).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.CertificateUntrusted));
			Assert.That(_catalog.Snapshot.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_signing_key_the_cached_snapshot_revoked_cannot_publish_a_new_snapshot()
	{
		var retired = TestPki.IssueCertificate(subjectKind: "service", keyUsage: _registryUsage);
		var revoking = Registry(sequence: 1);
		revoking.RevokedKeyIds.Add(retired.CertificateId);
		await Create(revoking).Refresh();

		var result = await Create(Registry(sequence: 2, certificate: retired)).Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.SigningKeyRevoked));
			Assert.That(_catalog.Snapshot.Entries, Has.Count.EqualTo(3));
		});
	}

	[Test]
	public async Task A_failed_refresh_keeps_serving_the_last_verified_catalog_and_reports_the_failure()
	{
		var fixture = Registry();
		var refresher = Create(fixture);
		await refresher.Refresh();

		fixture.Offline = true;
		var result = await refresher.Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_catalog.Snapshot.Entries, Has.Count.EqualTo(3));
			Assert.That(refresher.Status.HasCatalog, Is.True);
			Assert.That(refresher.Status.LastError, Is.Not.Null);
			Assert.That(refresher.Status.Stale, Is.True);
		});
	}

	[Test]
	public async Task A_cached_tree_edited_on_disk_is_discarded_rather_than_trusted()
	{
		var refresher = Create(Registry());
		await refresher.Refresh();

		var indexPath = Path.Combine(_paths.StoreRegistryCurrentDirectory, "index.json");
		var original = await File.ReadAllTextAsync(indexPath);
		await File.WriteAllTextAsync(indexPath,
			original.Replace("\"com.acme.hue\"",
				"\"com.acme.hue\",\"com.evil.backdoor\"",
				StringComparison.Ordinal));

		var reloaded = Create(Registry());
		_catalog.Swap(StoreCatalogSnapshot.Empty);
		await reloaded.LoadCachedRegistry();

		Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Does.Not.Contain("com.evil.backdoor"));
	}

	[Test]
	public async Task An_older_but_validly_signed_tree_dropped_into_the_cache_is_refused_on_load()
	{
		var certificate = TestPki.IssueCertificate(subjectKind: "service",
			keyUsage: _registryUsage);
		var refresher = Create(Registry(sequence: 7, certificate: certificate));
		await refresher.Refresh();

		var older = Registry(sequence: 6, certificate: certificate);
		older.PluginIds.Clear();
		older.IconPackIds.Clear();
		older.ProfileTemplateIds.Clear();
		older.AddPackage("plugin", "com.acme.hue", "2.0.0");
		await WriteTreeToCache(older);

		_catalog.Swap(StoreCatalogSnapshot.Empty);
		await Create(Registry(certificate: certificate)).LoadCachedRegistry();

		Assert.That(_catalog.Snapshot.Entries, Is.Empty);
	}

	[Test]
	public async Task A_snapshot_older_than_the_freshness_bound_is_reported_stale()
	{
		var refresher = Create(Registry(), maxSnapshotAge: TimeSpan.FromHours(1));
		await refresher.Refresh();

		_time.Now = _now.AddHours(2);

		Assert.Multiple(() =>
		{
			Assert.That(refresher.Status.HasCatalog, Is.True);
			Assert.That(refresher.Status.Stale, Is.True);
		});
	}

	private async Task WriteTreeToCache(StoreRegistryFixture fixture)
	{
		var factory = fixture.Build();
		using var client = factory.CreateClient("test");
		var root = _paths.StoreRegistryCurrentDirectory;
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}

		Directory.CreateDirectory(root);
		var paths = fixture.Served.Keys
			.Concat(["registry-manifest.json", "registry-signature.json"])
			.Distinct(StringComparer.Ordinal);

		foreach (var relative in paths)
		{
			var response = await client.GetAsync(new Uri(new Uri(StoreRegistryFixture.BaseUrl), relative));
			if (!response.IsSuccessStatusCode)
			{
				continue;
			}

			var destination = Path.Combine(root, Path.Combine(relative.Split('/')));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await File.WriteAllBytesAsync(destination, await response.Content.ReadAsByteArrayAsync());
		}
	}
}

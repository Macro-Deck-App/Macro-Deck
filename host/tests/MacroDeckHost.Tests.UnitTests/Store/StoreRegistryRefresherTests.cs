using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Events;
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
	private StoreRegistryRefreshTracker _tracker = null!;
	private StubHostApplicationLifetime _lifetime = null!;
	private RecordingMediator _mediator = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreRegistryDirectory);
		Directory.CreateDirectory(_paths.StoreRegistryStagingDirectory);
		_time = new ManualTimeProvider { Now = _now };
		_catalog = new StoreCatalog();
		_state = new JsonStoreRegistryStateStore(_paths, Log.Logger);
		_tracker = new StoreRegistryRefreshTracker(_time);
		_lifetime = new StubHostApplicationLifetime();
	}

	[TearDown]
	public void TearDown()
	{
		_lifetime.Dispose();
		_paths.Cleanup();
	}

	private StoreRegistryRefresher Create(StoreRegistryFixture fixture,
		TimeSpan? maxSnapshotAge = null,
		IReadOnlyList<TimeSpan>? retryDelays = null,
		Mediator.IMediator? mediator = null) =>
		Create(fixture.Build(), maxSnapshotAge, retryDelays, mediator);

	private StoreRegistryRefresher Create(IHttpClientFactory transport,
		TimeSpan? maxSnapshotAge = null,
		IReadOnlyList<TimeSpan>? retryDelays = null,
		Mediator.IMediator? mediator = null) =>
		new(transport,
			StoreRegistryOptions.Default with
			{
				BaseUrl = new Uri(StoreRegistryFixture.BaseUrl),
				RootPublicKeyOverride = TestPki.Root.PublicKey,
				MaxSnapshotAge = maxSnapshotAge ?? StoreRegistryOptions.Default.MaxSnapshotAge,
				UpdateRaceRetryDelays = retryDelays ?? []
			},
			_state,
			new StoreRegistryReader(Log.Logger),
			_catalog,
			_paths,
			_time,
			_tracker,
			mediator ?? _mediator,
			_lifetime,
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

	[Test]
	public async Task Overlapping_refresh_requests_share_one_run_instead_of_refreshing_twice()
	{
		var fixture = Registry();
		fixture.ManifestHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var refresher = Create(fixture);

		var first = refresher.Refresh();
		await fixture.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var second = refresher.Refresh(StoreRegistryRefreshTrigger.Scheduled);
		var statusWhileRunning = refresher.Status;
		var runWhileRunning = _tracker.Current!;
		fixture.ManifestHold.SetResult();
		var results = await Task.WhenAll(first, second);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.ManifestRequests, Is.EqualTo(1));
			Assert.That(results.Select(result => result.Success), Is.All.True);
			Assert.That(statusWhileRunning.Refreshing, Is.True);
			Assert.That(runWhileRunning.State, Is.EqualTo(StoreRegistryRefreshRunState.Running));
			Assert.That(runWhileRunning.Trigger, Is.EqualTo(StoreRegistryRefreshTrigger.Manual));
			Assert.That(_tracker.Current!.Id, Is.EqualTo(runWhileRunning.Id));
			Assert.That(_tracker.Current.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
			Assert.That(refresher.Status.Refreshing, Is.False);
		});
	}

	[Test]
	public async Task A_caller_that_stops_waiting_does_not_cancel_the_refresh_others_are_waiting_for()
	{
		var fixture = Registry();
		fixture.ManifestHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var refresher = Create(fixture);
		using var leaving = new CancellationTokenSource();

		var abandoned = refresher.Refresh(leaving.Token);
		await fixture.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var waiting = refresher.Refresh();
		await leaving.CancelAsync();
		Assert.CatchAsync<OperationCanceledException>(async () => await abandoned);
		fixture.ManifestHold.SetResult();
		var result = await waiting;

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Is.EquivalentTo(_seededIds));
			Assert.That(_tracker.Current!.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
		});
	}

	[Test]
	public async Task A_refresh_requested_after_the_previous_one_finished_fetches_the_registry_again()
	{
		var fixture = Registry();
		using var refresher = Create(fixture);

		await refresher.Refresh();
		var firstRun = _tracker.Current!;
		await refresher.Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.ManifestRequests, Is.EqualTo(2));
			Assert.That(_tracker.Current!.Id, Is.Not.EqualTo(firstRun.Id));
		});
	}

	[Test]
	public async Task A_refresh_log_names_each_step_a_new_snapshot_goes_through_and_an_unchanged_one_skips()
	{
		using var refresher = Create(Registry());

		await refresher.Refresh();
		var applied = _tracker.Current!;
		await refresher.Refresh();
		var unchanged = _tracker.Current!;

		Assert.Multiple(() =>
		{
			Assert.That(applied.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
			Assert.That(applied.Entries.Select(entry => entry.Step), Is.EqualTo(new[]
			{
				StoreRegistryRefreshStep.Started,
				StoreRegistryRefreshStep.FetchingManifest,
				StoreRegistryRefreshStep.FetchingSignature,
				StoreRegistryRefreshStep.DownloadingFiles,
				StoreRegistryRefreshStep.Verifying,
				StoreRegistryRefreshStep.ReadingCatalog,
				StoreRegistryRefreshStep.Applied
			}));
			Assert.That(applied.FilesTotal, Is.GreaterThan(0));
			Assert.That(applied.FilesCompleted, Is.EqualTo(applied.FilesTotal));
			Assert.That(unchanged.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
			Assert.That(unchanged.Entries.Select(entry => entry.Step), Is.EqualTo(new[]
			{
				StoreRegistryRefreshStep.Started,
				StoreRegistryRefreshStep.FetchingManifest,
				StoreRegistryRefreshStep.FetchingSignature,
				StoreRegistryRefreshStep.UpToDate
			}));
		});
	}

	[Test]
	public async Task A_failed_refresh_ends_its_run_with_the_reason_and_the_status_every_session_should_show()
	{
		var fixture = Registry();
		using var refresher = Create(fixture);
		await refresher.Refresh();

		fixture.Offline = true;
		await refresher.Refresh();
		var run = _tracker.Current!;

		Assert.Multiple(() =>
		{
			Assert.That(run.State, Is.EqualTo(StoreRegistryRefreshRunState.Failed));
			Assert.That(run.Entries[^1].Step, Is.EqualTo(StoreRegistryRefreshStep.Failed));
			Assert.That(run.Entries[^1].Error, Is.EqualTo(RegistryRefreshError.NetworkFailure));
			Assert.That(run.Status!.LastError, Is.EqualTo(RegistryRefreshError.NetworkFailure));
			Assert.That(run.Status.Stale, Is.True);
			Assert.That(run.Status.Refreshing, Is.False);
		});
	}

	[Test]
	public async Task Shutting_down_during_a_refresh_ends_the_run_as_cancelled()
	{
		var fixture = Registry();
		fixture.ManifestHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var refresher = Create(fixture);

		var pending = refresher.Refresh();
		await fixture.ManifestRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.DoesNotThrow(refresher.Dispose);
		Assert.CatchAsync<OperationCanceledException>(async () => await pending);
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Current!.State, Is.EqualTo(StoreRegistryRefreshRunState.Cancelled));
			Assert.That(_tracker.Current.Entries[^1].Step, Is.EqualTo(StoreRegistryRefreshStep.Cancelled));
		});
	}

	[TestCase(ChangedFile, TestName = "A_file_already_from_the_next_update_is_waited_out_and_that_update_applied")]
	[TestCase("registry-signature.json", TestName = "A_signature_already_from_the_next_update_is_waited_out_and_that_update_applied")]
	[TestCase(DeletedFile, TestName = "A_file_the_next_update_deleted_is_waited_out_and_that_update_applied")]
	public async Task A_refresh_that_straddles_a_registry_update_waits_for_the_update_and_applies_it(string servedEarly)
	{
		var (older, newer) = Generations();
		var switched = false;
		older.ServeOverride = relative => switched || relative == servedEarly
			? StoreRegistryFixture.Serve(newer.BuiltTree, relative)
			: null;
		var refresher = Create(older, retryDelays: _productionDelays);

		var armed = _time.ScheduledCount;
		var pending = refresher.Refresh();
		await _time.WaitForScheduleAsync(armed).WaitAsync(TimeSpan.FromSeconds(10));
		switched = true;
		_time.Advance(TimeSpan.FromSeconds(30));
		var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(_state.Load(StoreRegistryFixture.BaseUrl)?.AcceptedSequence, Is.EqualTo(2));
			Assert.That(_catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.hue").History[0].Changelog,
				Is.EqualTo(ChangedChangelog));
			Assert.That(_tracker.Current!.Entries.Where(entry => entry.Step == StoreRegistryRefreshStep.WaitingForRegistryUpdate)
				.Select(entry => entry.Count), Is.EqualTo(new int?[] { 30 }));
			Assert.That(_tracker.Current.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
		});
	}

	[TestCase(ChangedFile, RegistryRefreshError.SizeMismatch)]
	[TestCase(DeletedFile, RegistryRefreshError.NetworkFailure)]
	public async Task A_mismatch_that_outlasts_every_wait_still_fails_with_its_own_error(string servedEarly,
		RegistryRefreshError expected)
	{
		var (older, newer) = Generations();
		older.ServeOverride = relative => relative == servedEarly
			? StoreRegistryFixture.Serve(newer.BuiltTree, relative)
			: null;
		var refresher = Create(older, retryDelays: _productionDelays);

		var result = await RefreshThroughWaits(refresher);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(expected));
			Assert.That(_catalog.Snapshot.Entries, Is.Empty);
			Assert.That(_state.Load(StoreRegistryFixture.BaseUrl), Is.Null);
			Assert.That(_tracker.Current!.Entries.Count(entry => entry.Step == StoreRegistryRefreshStep.WaitingForRegistryUpdate),
				Is.EqualTo(_productionDelays.Length));
		});
	}

	[Test]
	public async Task An_older_snapshot_served_after_a_newer_one_was_accepted_is_waited_out_but_never_accepted()
	{
		var (older, newer) = Generations();
		older.Build();
		var refresher = Create(newer, retryDelays: _productionDelays);
		await refresher.Refresh();
		newer.ServeOverride = relative => StoreRegistryFixture.Serve(older.BuiltTree, relative);

		var result = await RefreshThroughWaits(refresher);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.SequenceRollback));
			Assert.That(_state.Load(StoreRegistryFixture.BaseUrl)?.AcceptedSequence, Is.EqualTo(2));
			Assert.That(_catalog.Snapshot.Entries.Single(entry => entry.Id == "com.acme.hue").History[0].Changelog,
				Is.EqualTo(ChangedChangelog));
		});
	}

	[Test]
	public async Task A_failure_no_registry_update_can_cause_is_reported_at_once_without_waiting()
	{
		var certificate = TestPki.IssueCertificate(subjectKind: "service", keyUsage: _packageUsage);
		var refresher = Create(Registry(certificate: certificate), retryDelays: _productionDelays);
		var armed = _time.ScheduledCount;

		var result = await refresher.Refresh().WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(RegistryRefreshError.CertificateUntrusted));
			Assert.That(_time.ScheduledCount, Is.EqualTo(armed));
			Assert.That(_tracker.Current!.Entries.Select(entry => entry.Step),
				Does.Not.Contain(StoreRegistryRefreshStep.WaitingForRegistryUpdate));
		});
	}

	[Test]
	public async Task Shutting_down_while_waiting_for_a_registry_update_ends_the_run_as_cancelled_and_announces_nothing()
	{
		var (older, newer) = Generations();
		older.ServeOverride = relative => relative == ChangedFile
			? StoreRegistryFixture.Serve(newer.BuiltTree, relative)
			: null;
		var refresher = Create(older, retryDelays: _productionDelays);

		var armed = _time.ScheduledCount;
		var pending = refresher.Refresh();
		await _time.WaitForScheduleAsync(armed).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.DoesNotThrow(refresher.Dispose);
		Assert.CatchAsync<OperationCanceledException>(async () => await pending);
		Assert.Multiple(() =>
		{
			Assert.That(_tracker.Current!.State, Is.EqualTo(StoreRegistryRefreshRunState.Cancelled));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task A_refresh_whose_caller_stopped_waiting_still_announces_its_outcome_once()
	{
		var (older, newer) = Generations();
		var switched = false;
		older.ServeOverride = relative => switched || relative == ChangedFile
			? StoreRegistryFixture.Serve(newer.BuiltTree, relative)
			: null;
		var refresher = Create(older, retryDelays: _productionDelays);
		using var leaving = new CancellationTokenSource();

		var armed = _time.ScheduledCount;
		var abandoned = refresher.Refresh(leaving.Token);
		await _time.WaitForScheduleAsync(armed).WaitAsync(TimeSpan.FromSeconds(10));
		await leaving.CancelAsync();
		Assert.CatchAsync<OperationCanceledException>(async () => await abandoned);
		switched = true;
		_time.Advance(TimeSpan.FromSeconds(30));
		await Eventually(() => _mediator.Published.Count > 0);

		var announced = _mediator.Published.OfType<StoreRegistryRefreshedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(announced.Status.Sequence, Is.EqualTo(2));
			Assert.That(announced.Status.LastError, Is.Null);
			Assert.That(_mediator.Published, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_failed_refresh_is_announced_once_with_the_failure_it_ended_with()
	{
		var fixture = Registry();
		fixture.Offline = true;

		await Create(fixture).Refresh();

		var announced = _mediator.Published.OfType<StoreRegistryRefreshedNotification>().Single();
		Assert.That(announced.Status.LastError, Is.EqualTo(RegistryRefreshError.NetworkFailure));
	}

	[Test]
	public async Task A_listener_that_throws_does_not_turn_an_applied_refresh_into_a_failure()
	{
		var refresher = Create(Registry(), mediator: new ThrowingPublishMediator());

		var result = await refresher.Refresh();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_tracker.Current!.State, Is.EqualTo(StoreRegistryRefreshRunState.Succeeded));
			Assert.That(_catalog.Snapshot.Entries.Select(entry => entry.Id), Is.EquivalentTo(_seededIds));
		});
	}

	private const string ChangedFile = "plugins/com.acme.hue/versions/2.1.0/changelog.md";
	private const string ChangedChangelog = "# 2.1.0 with notes that grew in the update";
	private const string DeletedFile = "plugins/com.acme.hue/versions/2.0.0/changelog.md";

	private static readonly TimeSpan[] _productionDelays = [.. StoreRegistryOptions.Default.UpdateRaceRetryDelays];

	private static (StoreRegistryFixture Older, StoreRegistryFixture Newer) Generations()
	{
		var certificate = TestPki.IssueCertificate(subjectKind: "service", keyUsage: _registryUsage);
		var older = Registry(sequence: 1, certificate: certificate);
		older.AddVersion("plugins", "com.acme.hue", "2.0.0");
		var newer = Registry(sequence: 2, certificate: certificate);
		newer.WriteText(ChangedFile, ChangedChangelog);
		newer.Build();
		return (older, newer);
	}

	private async Task<MacroDeckHost.Domain.Common.Result<RegistryRefreshError>> RefreshThroughWaits(
		StoreRegistryRefresher refresher)
	{
		var armed = _time.ScheduledCount;
		var pending = refresher.Refresh();
		foreach (var delay in _productionDelays)
		{
			await _time.WaitForScheduleAsync(armed).WaitAsync(TimeSpan.FromSeconds(10));
			armed = _time.ScheduledCount;
			_time.Advance(delay);
		}

		return await pending.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private static async Task Eventually(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition did not become true within 10 seconds.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class ThrowingPublishMediator : Mediator.IMediator
	{
		public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
			where TNotification : Mediator.INotification =>
			throw new InvalidOperationException("listener failed");

		public ValueTask Publish(object notification, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("listener failed");

		public ValueTask<TResponse> Send<TResponse>(Mediator.IRequest<TResponse> request,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(Mediator.ICommand<TResponse> command,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(Mediator.IQuery<TResponse> query,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(Mediator.IStreamRequest<TResponse> request,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(Mediator.IStreamCommand<TResponse> command,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(Mediator.IStreamQuery<TResponse> query,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
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

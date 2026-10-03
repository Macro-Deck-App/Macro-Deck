using System.Collections.Concurrent;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Trust;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Connect;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StoreRatingPromptServiceTests
{
	private static readonly string Official = StoreRegistryOptions.OfficialOrigin;

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;
	private FakeStorePlatformClient _platform = null!;
	private FakeConnectSessionService _session = null!;
	private FakeTimeProvider _time = null!;
	private MemoryPreferences _preferences = null!;
	private ServiceProvider _services = null!;
	private StoreRatingPromptService _prompt = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_platform = new FakeStorePlatformClient();
		_session = new FakeConnectSessionService { Current = FakeConnectSessionService.SignedIn(null) };
		_time = new FakeTimeProvider();
		_preferences = new MemoryPreferences();
		_services = new ServiceCollection()
			.AddScoped<IAppPreferenceRepository>(_ => _preferences)
			.AddScoped<IAppPreferenceService>(provider => new AppPreferenceService(
				provider.GetRequiredService<IAppPreferenceRepository>(),
				new BuildEnvironmentStub(),
				new FakeHostListenerState()))
			.BuildServiceProvider();

		var options = StoreRegistryOptions.Default;
		var query = new StoreCatalogQueryService(_catalog,
			new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None),
			_installations,
			new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None),
			new InstalledPluginSigners());
		var packages = new StoreOfficialPackages(_catalog, query, _installations, options);
		var reviews = new StoreReviewService(_platform, packages, _session, StorePlatformOptions.Default, _time);
		_prompt = new StoreRatingPromptService(packages,
			query,
			_installations,
			reviews,
			_session,
			_services.GetRequiredService<IServiceScopeFactory>(),
			_time);
	}

	[TearDown]
	public void TearDown()
	{
		_prompt.Dispose();
		_services.Dispose();
		_paths.Cleanup();
	}

	[Test]
	public async Task An_unrated_package_installed_for_a_week_is_offered()
	{
		Install("com.acme.icons", daysAgo: 8);

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Ready, Is.True);
			Assert.That(response.Candidate?.Id, Is.EqualTo("com.acme.icons"));
			Assert.That(response.Candidate?.Name, Is.EqualTo("Icons com.acme.icons"));
			Assert.That(response.Candidate?.Kind, Is.EqualTo(StoreExtensionKind.IconPack));
		});
	}

	[TestCase(6.99, false)]
	[TestCase(7, true)]
	public async Task A_package_needs_to_have_been_installed_for_seven_days(double days, bool offered)
	{
		Install("com.acme.icons", daysAgo: days);

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate is not null, Is.EqualTo(offered));
	}

	[Test]
	public async Task Nothing_is_offered_while_the_catalog_is_still_loading()
	{
		Install("com.acme.icons", daysAgo: 30);
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 0, Entries = [] });

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Ready, Is.False);
			Assert.That(response.Candidate, Is.Null);
		});
	}

	[Test]
	public async Task Nothing_is_offered_unless_the_user_is_signed_in()
	{
		Install("com.acme.icons", daysAgo: 30);

		var offered = new List<bool>();
		foreach (var status in new[] { ConnectAccountStatus.SignedOut, ConnectAccountStatus.Suspended, ConnectAccountStatus.ReauthenticationRequired })
		{
			_session.Current = FakeConnectSessionService.SignedIn(null) with { Status = status };
			offered.Add((await _prompt.GetPrompt(CancellationToken.None)).Candidate is not null);
		}

		Assert.That(offered, Is.EqualTo(new[] { false, false, false }));
	}

	[Test]
	public async Task Nothing_is_offered_once_the_user_turned_the_prompt_off()
	{
		Install("com.acme.icons", daysAgo: 30);
		await _services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider
			.GetRequiredService<IAppPreferenceService>()
			.SetExtensions(null, null, null, null, askForRatings: false);

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task A_package_the_user_already_rated_is_never_offered()
	{
		Install("com.acme.icons", daysAgo: 30);
		_platform.OwnReviews["com.acme.icons"] = FakeStorePlatformClient.Own("com.acme.icons", 4, null, null);

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task A_rated_package_is_skipped_in_favour_of_an_unrated_one()
	{
		Install("com.acme.rated", daysAgo: 60);
		Install("com.acme.fresh", daysAgo: 10);
		_platform.OwnReviews["com.acme.rated"] = FakeStorePlatformClient.Own("com.acme.rated", 5, null, null);

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate?.Id, Is.EqualTo("com.acme.fresh"));
	}

	[Test]
	public async Task The_five_day_interval_starts_when_a_prompt_was_shown_not_when_it_was_offered()
	{
		Install("com.acme.icons", daysAgo: 30);

		await _prompt.GetPrompt(CancellationToken.None);
		_time.Advance(TimeSpan.FromDays(20));
		var stillOffered = await _prompt.GetPrompt(CancellationToken.None);
		await _prompt.MarkShown(StoreExtensionKind.IconPack, "com.acme.icons", CancellationToken.None);
		_time.Advance(TimeSpan.FromDays(5) - TimeSpan.FromTicks(1));
		var tooSoon = await _prompt.GetPrompt(CancellationToken.None);
		_time.Advance(TimeSpan.FromTicks(1));
		var due = await _prompt.GetPrompt(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(stillOffered.Candidate, Is.Not.Null);
			Assert.That(tooSoon.Candidate, Is.Null);
			Assert.That(due.Candidate?.Id, Is.EqualTo("com.acme.icons"));
		});
	}

	[Test]
	public async Task A_dismissed_package_is_offered_again_but_other_packages_come_first()
	{
		Install("com.acme.old", daysAgo: 60);
		Install("com.acme.new", daysAgo: 10);

		var first = await _prompt.GetPrompt(CancellationToken.None);
		await _prompt.MarkShown(first.Candidate!.Kind, first.Candidate.Id, CancellationToken.None);
		_time.Advance(TimeSpan.FromDays(5));
		var second = await _prompt.GetPrompt(CancellationToken.None);
		await _prompt.MarkShown(second.Candidate!.Kind, second.Candidate.Id, CancellationToken.None);
		_time.Advance(TimeSpan.FromDays(5));
		var third = await _prompt.GetPrompt(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first.Candidate.Id, Is.EqualTo("com.acme.old"));
			Assert.That(second.Candidate.Id, Is.EqualTo("com.acme.new"));
			Assert.That(third.Candidate?.Id, Is.EqualTo("com.acme.old"));
		});
	}

	[Test]
	public async Task A_package_from_a_custom_registry_is_never_offered()
	{
		Install("com.acme.icons", daysAgo: 30, origin: "https://registry.example/");

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task A_package_without_an_install_record_is_never_offered()
	{
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [Entry("com.acme.icons", StoreExtensionKind.IconPack)] });

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task A_package_removed_from_the_store_after_installing_is_never_offered()
	{
		Install("com.acme.icons", daysAgo: 30);
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 2,
			Entries = [Entry("com.acme.icons", StoreExtensionKind.IconPack)],
			RemovedPackages = [new StoreRemovedPackage { Id = "com.acme.icons" }]
		});

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task An_unreachable_platform_offers_nothing()
	{
		Install("com.acme.icons", daysAgo: 30);
		_platform.EntitlementFailure = StorePlatformFailure.Unavailable;

		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate, Is.Null);
	}

	[Test]
	public async Task Reporting_a_package_that_is_not_installed_does_not_start_the_interval()
	{
		Install("com.acme.icons", daysAgo: 30);

		await _prompt.MarkShown(StoreExtensionKind.IconPack, "com.acme.unknown", CancellationToken.None);
		var response = await _prompt.GetPrompt(CancellationToken.None);

		Assert.That(response.Candidate?.Id, Is.EqualTo("com.acme.icons"));
	}

	[Test]
	public async Task An_update_does_not_restart_the_seven_days_but_an_uninstall_does()
	{
		Install("com.acme.icons", daysAgo: 30);
		Install("com.acme.icons", daysAgo: 0);
		var afterUpdate = await _prompt.GetPrompt(CancellationToken.None);

		_installations.Delete(StoreExtensionKind.IconPack, "com.acme.icons");
		Install("com.acme.icons", daysAgo: 0);
		var afterReinstall = await _prompt.GetPrompt(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(afterUpdate.Candidate?.Id, Is.EqualTo("com.acme.icons"));
			Assert.That(afterReinstall.Candidate, Is.Null);
		});
	}

	[Test]
	public void A_record_from_another_registry_does_not_inherit_the_install_time()
	{
		Install("com.acme.icons", daysAgo: 30, origin: "https://registry.example/");
		Install("com.acme.icons", daysAgo: 0);

		var record = _installations.Find(StoreExtensionKind.IconPack, "com.acme.icons");

		Assert.That(_time.GetUtcNow() - record!.InstalledAt, Is.LessThan(TimeSpan.FromMinutes(1)));
	}

	private void Install(string id, double daysAgo, string? origin = null)
	{
		var entries = _catalog.Snapshot.Entries.Where(entry => entry.Id != id).Append(Entry(id, StoreExtensionKind.IconPack)).ToList();
		_catalog.Swap(new StoreCatalogSnapshot { Sequence = _catalog.Snapshot.Sequence + 1, Entries = entries });
		_installations.Save(new StoreInstallationRecord
		{
			Origin = origin ?? Official,
			Kind = StoreExtensionKind.IconPack,
			PackageId = id,
			Version = "1.0.0",
			InstalledAt = _time.GetUtcNow() - TimeSpan.FromDays(daysAgo)
		});
	}

	private static StoreCatalogEntry Entry(string id, StoreExtensionKind kind) => new()
	{
		Kind = kind,
		Id = id,
		Name = $"Icons {id}",
		LatestVersion = "1.0.0",
		LatestRelease = new StoreReleaseManifest
		{
			Version = "1.0.0", ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"), Sha256 = new string('a', 64), Size = 16
		}
	};

	private sealed class BuildEnvironmentStub : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		private readonly ConcurrentDictionary<string, string> _values = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);

		public Task SetValue(string key, string value)
		{
			_values[key] = value;
			return Task.CompletedTask;
		}
	}
}

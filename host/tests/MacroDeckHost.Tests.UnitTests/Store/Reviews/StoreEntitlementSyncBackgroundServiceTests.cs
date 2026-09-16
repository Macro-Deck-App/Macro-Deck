using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Connect;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StoreEntitlementSyncBackgroundServiceTests
{
	private FakeConnectSessionService _session = null!;
	private StoreOperationTracker _operations = null!;
	private StoreRegistryRefreshTracker _refreshes = null!;
	private FakeStoreOfficialPackages _packages = null!;
	private FakeStorePlatformClient _platform = null!;
	private FakeTimeProvider _time = null!;
	private StoreEntitlementSyncBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_session = new FakeConnectSessionService();
		_time = new FakeTimeProvider();
		_operations = new StoreOperationTracker(new InMemoryStoreOperationStore(), _time);
		_refreshes = new StoreRegistryRefreshTracker(_time);
		_packages = new FakeStoreOfficialPackages { Installed = ["com.acme.hue", "com.acme.icons"] };
		_platform = new FakeStorePlatformClient();
		_service = new StoreEntitlementSyncBackgroundService(_session,
			_operations,
			_refreshes,
			_packages,
			_platform,
			_time,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
	}

	[Test]
	public async Task Signing_in_claims_every_installed_official_package()
	{
		await _service.StartAsync(CancellationToken.None);

		_session.Publish(FakeConnectSessionService.SignedIn(null));

		await WaitForClaims(1);
		Assert.That(_platform.Claims.Single(), Is.EquivalentTo(new[] { "com.acme.hue", "com.acme.icons" }));
	}

	[Test]
	public async Task A_host_that_starts_already_signed_in_claims_once()
	{
		_session.Current = FakeConnectSessionService.SignedIn(null);
		await _service.StartAsync(CancellationToken.None);

		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await WaitForClaims(1);
		await Task.Delay(200);

		Assert.That(_platform.Claims, Has.Count.EqualTo(1), "a repeated SignedIn snapshot is not a new sign-in");
	}

	[Test]
	public async Task Signing_out_and_in_again_claims_again()
	{
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await WaitForClaims(1);

		_session.Publish(ConnectSessionSnapshot.SignedOut);
		_session.Publish(FakeConnectSessionService.SignedIn(null));

		await WaitForClaims(2);
	}

	[Test]
	public async Task An_install_or_update_that_completes_while_signed_in_claims_that_package()
	{
		_packages.Installed = [];
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await Task.Delay(100);

		_packages.Installed = ["com.acme.hue"];
		Complete(StoreOperationKind.Install, "com.acme.hue");
		await WaitForClaims(1);

		Complete(StoreOperationKind.Update, "com.acme.hue");
		await WaitForClaims(2);

		Assert.That(_platform.Claims.Select(claim => claim.Single()), Is.All.EqualTo("com.acme.hue"));
	}

	[Test]
	public async Task An_install_while_signed_out_claims_nothing()
	{
		await _service.StartAsync(CancellationToken.None);

		Complete(StoreOperationKind.Install, "com.acme.hue");
		await Task.Delay(200);

		Assert.That(_platform.Claims, Is.Empty);
	}

	[Test]
	public async Task A_catalog_that_is_not_loaded_yet_is_claimed_once_it_is()
	{
		_packages.CatalogLoaded = false;
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await Task.Delay(200);
		Assert.That(_platform.Claims, Is.Empty);

		_packages.CatalogLoaded = true;
		await AdvanceUntilClaims(StoreEntitlementSyncBackgroundService.CatalogWaitInterval, 1);
	}

	[Test]
	public async Task A_claim_that_fails_while_offline_is_sent_when_the_connection_returns()
	{
		_platform.ClaimResult = _ => StorePlatformResult.Fail<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(
			StorePlatformFailure.Unavailable);
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(Offline(FakeConnectSessionService.SignedIn(null)));
		await WaitForClaims(1);

		_platform.ClaimResult = ids => StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(
			ids.ToDictionary(id => id, _ => StoreEntitlementClaimStatus.Claimed));
		_session.Publish(FakeConnectSessionService.SignedIn(null));

		await WaitForClaims(2);
		await Task.Delay(200);
		Assert.That(_platform.Claims, Has.Count.EqualTo(2), "a successful claim is not repeated");
	}

	[Test]
	public async Task A_rate_limited_claim_waits_for_retry_after_instead_of_looping()
	{
		var retryAfter = TimeSpan.FromMinutes(5);
		_platform.ClaimResult = _ => StorePlatformResult.Fail<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(
			StorePlatformFailure.Cooldown,
			retryAfter);
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await WaitForClaims(1);

		_time.Advance(TimeSpan.FromMinutes(4));
		await Task.Delay(200);
		Assert.That(_platform.Claims, Has.Count.EqualTo(1));

		await AdvanceUntilClaims(TimeSpan.FromMinutes(1), 2);
	}

	[Test]
	public async Task A_package_the_platform_does_not_know_does_not_keep_the_claim_pending()
	{
		_platform.ClaimResult = ids => StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(
			ids.ToDictionary(id => id, _ => StoreEntitlementClaimStatus.Unavailable));
		await _service.StartAsync(CancellationToken.None);
		_session.Publish(FakeConnectSessionService.SignedIn(null));
		await WaitForClaims(1);

		_time.Advance(TimeSpan.FromHours(2));
		await Task.Delay(200);

		Assert.That(_platform.Claims, Has.Count.EqualTo(1));
	}

	private void Complete(StoreOperationKind kind, string packageId)
	{
		var operation = _operations.Create(kind, StoreExtensionKind.Plugin, packageId, "1.0.0", packageId, null);
		_operations.Transition(operation.Id, StoreOperationState.Completed);
	}

	private static ConnectSessionSnapshot Offline(ConnectSessionSnapshot snapshot) =>
		snapshot with { Connectivity = ConnectConnectivity.Offline, OfflineSince = DateTimeOffset.UnixEpoch };

	private Task WaitForClaims(int count) =>
		ConnectTestHarness.WaitUntil(() => _platform.Claims.Count >= count, $"expected {count} claims");

	private async Task AdvanceUntilClaims(TimeSpan step, int count)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (_platform.Claims.Count < count && DateTime.UtcNow < deadline)
		{
			_time.Advance(step);
			await Task.Delay(50);
		}

		Assert.That(_platform.Claims, Has.Count.EqualTo(count));
	}
}

using System.Collections.Concurrent;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Licensing;
using MacroDeckHost.Tests.UnitTests.Connect;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

internal sealed partial class CompanionLicenseServiceTests
{
	[Test]
	public async Task A_license_issued_from_a_proof_this_host_forwarded_is_offered_to_an_empty_account()
	{
		var fixture = new Fixture();
		var issued = await IssueFromProofAsync(fixture, "license-bought");

		var result = await fixture.Service.ReconcileAccountAsync(null, default);

		Assert.Multiple(() =>
		{
			Assert.That(result.UploadToken, Is.EqualTo(issued));
			Assert.That(result.UploadLicenseId, Is.EqualTo("license-bought"));
		});
	}

	[Test]
	public async Task A_license_transferred_from_a_macro_deck_2_purchase_is_offered_to_an_empty_account()
	{
		var fixture = new Fixture();
		var issued = Sign(ProductionKey, ProductionKeyId, "license-md2");
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(issued);
		await TransferAsync(fixture, Md2Purchase("app-transaction-1"));

		var result = await fixture.Service.ReconcileAccountAsync(null, default);

		Assert.That(result.UploadToken, Is.EqualTo(issued));
	}

	[Test]
	public async Task A_license_only_taken_over_from_a_companion_is_never_offered_to_the_account()
	{
		var fixture = new Fixture();
		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, "license-friend") },
			default);

		var result = await fixture.Service.ReconcileAccountAsync(null, default);

		Assert.Multiple(async () =>
		{
			Assert.That(result.UploadToken, Is.Null);
			Assert.That((await fixture.Service.GetStatusAsync(default)).LicenseId, Is.EqualTo("license-friend"));
		});
	}

	[Test]
	public async Task A_license_stored_before_account_sync_existed_is_offered_once_the_host_upgrades()
	{
		var fixture = new Fixture();
		var stored = Sign(ProductionKey, ProductionKeyId, "license-old");
		await fixture.Repository.SetValue(CompanionLicenseService.TokenKey, stored);
		fixture.Restart();

		var result = await fixture.Service.ReconcileAccountAsync(null, default);

		Assert.That(result.UploadToken, Is.EqualTo(stored));
	}

	[Test]
	public async Task A_companion_license_arriving_first_after_the_upgrade_is_not_taken_for_an_old_one()
	{
		var fixture = new Fixture();
		fixture.Restart();

		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, "license-friend") },
			default);
		fixture.Restart();
		var result = await fixture.Service.ReconcileAccountAsync(null, default);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Repository.Values[CompanionLicenseService.AccountEligibleIdKey],
				Is.EqualTo(CompanionLicenseService.NoAccountEligibleId));
			Assert.That(result.UploadToken, Is.Null);
		});
	}

	[Test]
	public async Task The_account_license_is_stored_and_pushed_to_companions_when_the_host_has_none()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);
		var account = Sign(ProductionKey, ProductionKeyId, "license-account");

		var result = await fixture.Service.ReconcileAccountAsync(account, default);

		var pushes = fixture.Harness.Transport.ConnectionMessages
			.Where(message => message.Message is CompanionLicenseEvent)
			.ToList();
		Assert.Multiple(async () =>
		{
			Assert.That(result.UploadToken, Is.Null);
			Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(account));
			Assert.That(pushes.Select(push => push.ConnectionId), Is.EqualTo(new[] { "connection-a" }));
			Assert.That(pushes.Select(push => ((CompanionLicenseEvent)push.Message).License), Is.All.EqualTo(account));
			Assert.That((await fixture.Service.GetStatusAsync(default)).AccountSync,
				Is.EqualTo(CompanionLicenseAccountSync.Synced));
		});
	}

	[Test]
	public async Task Two_valid_licenses_are_both_left_alone()
	{
		var fixture = new Fixture();
		var local = await IssueFromProofAsync(fixture, "license-local");

		var result = await fixture.Service.ReconcileAccountAsync(Sign(ProductionKey, ProductionKeyId, "license-account"),
			default);

		Assert.Multiple(() =>
		{
			Assert.That(result.UploadToken, Is.Null);
			Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(local));
		});
	}

	[Test]
	public async Task A_revoked_local_license_is_replaced_by_the_valid_account_license()
	{
		var fixture = new Fixture();
		await IssueFromProofAsync(fixture, HexId(1));
		fixture.Platform.RevokedIds = [HexId(1)];
		fixture.Time.Advance(CompanionLicenseService.RevocationRefreshInterval);
		await fixture.Service.RunDueWorkAsync(default);
		var account = Sign(ProductionKey, ProductionKeyId, HexId(2));

		await fixture.Service.ReconcileAccountAsync(account, default);

		Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(account));
	}

	[Test]
	public async Task A_revoked_account_license_is_replaced_by_the_valid_local_one()
	{
		var fixture = new Fixture();
		fixture.Platform.RevokedIds = [HexId(2)];
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest(), default);
		await fixture.Service.RunDueWorkAsync(default);
		var local = await IssueFromProofAsync(fixture, HexId(1));

		var result = await fixture.Service.ReconcileAccountAsync(Sign(ProductionKey, ProductionKeyId, HexId(2)), default);

		Assert.That(result.UploadToken, Is.EqualTo(local));
	}

	[Test]
	public async Task A_production_account_license_replaces_a_local_test_license()
	{
		var fixture = new Fixture();
		fixture.Preferences.DeveloperMode = true;
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = TestKeyLicenses.Sign() }, default);
		var account = Sign(ProductionKey, ProductionKeyId, "license-account");

		await fixture.Service.ReconcileAccountAsync(account, default);

		Assert.That(fixture.Repository.Values[CompanionLicenseService.TokenKey], Is.EqualTo(account));
	}

	[Test]
	public async Task Every_change_of_the_stored_license_is_announced()
	{
		var fixture = new Fixture();
		var changes = 0;
		fixture.Service.LicenseChanged += (_, _) => Interlocked.Increment(ref changes);

		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, HexId(1)) },
			default);
		var afterCompanion = changes;
		fixture.Platform.RevokedIds = [HexId(1)];
		await fixture.Service.RunDueWorkAsync(default);
		var afterRevocation = changes;
		await fixture.Service.ReconcileAccountAsync(Sign(ProductionKey, ProductionKeyId, HexId(2)), default);
		var afterAccount = changes;

		Assert.Multiple(() =>
		{
			Assert.That(afterCompanion, Is.EqualTo(1));
			Assert.That(afterRevocation, Is.EqualTo(2));
			Assert.That(afterAccount, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task The_status_tells_a_signed_out_owner_to_sign_in_but_not_for_a_companion_license()
	{
		var owned = new Fixture();
		await IssueFromProofAsync(owned, "license-bought");
		await owned.Service.ReportAccountSignedOutAsync(default);
		var borrowed = new Fixture();
		await borrowed.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, "license-friend") },
			default);
		await borrowed.Service.ReportAccountSignedOutAsync(default);

		Assert.Multiple(async () =>
		{
			Assert.That((await owned.Service.GetStatusAsync(default)).AccountSync,
				Is.EqualTo(CompanionLicenseAccountSync.SignedOut));
			Assert.That((await borrowed.Service.GetStatusAsync(default)).AccountSync,
				Is.EqualTo(CompanionLicenseAccountSync.Unknown));
		});
	}

	[Test]
	public async Task Signing_in_saves_the_hosts_own_license_to_an_empty_account()
	{
		var fixture = new Fixture();
		var issued = await IssueFromProofAsync(fixture, "license-bought");
		await using var sync = new SyncHarness(fixture);

		await sync.SignInAsync();
		await Until(() => sync.Account.License == issued);

		Assert.That(sync.Account.Puts, Is.EqualTo(new[] { issued }));
	}

	[Test]
	public async Task A_license_saved_from_another_computer_arrives_during_the_long_poll()
	{
		var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Tablet");
		await fixture.Harness.ReportAsync("connection-a", device);
		await using var sync = new SyncHarness(fixture);
		await sync.SignInAsync();
		await Until(() => sync.Account.WaitingPolls > 0);
		var fromElsewhere = Sign(ProductionKey, ProductionKeyId, "license-elsewhere");

		sync.Account.Store(fromElsewhere);
		await Until(() => fixture.Repository.Values.GetValueOrDefault(CompanionLicenseService.TokenKey) == fromElsewhere);

		Assert.Multiple(() =>
		{
			Assert.That(sync.Account.Puts, Is.Empty);
			Assert.That(fixture.Harness.Transport.ConnectionMessages.Select(message => message.Message),
				Has.Some.InstanceOf<CompanionLicenseEvent>());
		});
	}

	[Test]
	public async Task A_signed_out_host_never_calls_the_account()
	{
		var fixture = new Fixture();
		await IssueFromProofAsync(fixture, "license-bought");
		await using var sync = new SyncHarness(fixture);
		await sync.StartAsync();

		await Until(async () =>
			(await fixture.Service.GetStatusAsync(default)).AccountSync == CompanionLicenseAccountSync.SignedOut);

		Assert.That(sync.Account.Calls, Is.Zero);
	}

	[Test]
	public async Task A_license_issued_while_polling_is_saved_without_waiting_for_the_poll()
	{
		var fixture = new Fixture();
		await using var sync = new SyncHarness(fixture);
		await sync.SignInAsync();
		await Until(() => sync.Account.WaitingPolls > 0);

		var issued = await IssueFromProofAsync(fixture, "license-bought");
		await Until(() => sync.Account.License == issued);

		Assert.That(sync.Account.Puts, Is.EqualTo(new[] { issued }));
	}

	[Test]
	public async Task Signing_out_abandons_the_poll_and_stops_calling()
	{
		var fixture = new Fixture();
		await using var sync = new SyncHarness(fixture);
		await sync.SignInAsync();
		await Until(() => sync.Account.WaitingPolls > 0);

		sync.Session.Publish(ConnectSessionSnapshot.SignedOut);
		await Until(() => sync.Account.WaitingPolls == 0);
		var calls = sync.Account.Calls;
		fixture.Time.Advance(TimeSpan.FromMinutes(10));
		await Task.Delay(100);

		Assert.That(sync.Account.Calls, Is.EqualTo(calls));
	}

	[Test]
	public async Task A_sign_out_racing_the_start_of_a_sync_does_not_stop_later_syncs()
	{
		var fixture = new Fixture();
		var session = new RacingSession(signOutOnRead: 2);
		var account = new ScriptedAccount();
		using var worker = new CompanionLicenseAccountSyncBackgroundService(session,
			fixture.Service,
			account,
			fixture.Time,
			() => 0,
			new LoggerConfiguration().CreateLogger());
		using var stop = new CancellationTokenSource();
		await worker.StartAsync(stop.Token);
		await Until(() => session.Reads > 3);

		Assert.DoesNotThrow(() => session.Publish(FakeConnectSessionService.SignedIn(null)));
		await Until(() => account.WaitingPolls > 0);

		await stop.CancelAsync();
		await worker.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task A_refused_save_is_not_repeated_while_nothing_changed()
	{
		var fixture = new Fixture();
		await IssueFromProofAsync(fixture, "license-bought");
		await using var sync = new SyncHarness(fixture);
		sync.Account.PutAnswer = _ => new PlatformAccountLicenseResult.Refused("invalid-license");
		sync.Account.ImmediatePolls = true;

		await sync.SignInAsync();
		for (var poll = 0; poll < 5; poll++)
		{
			var calls = sync.Account.Calls;
			await Until(() => sync.Account.Calls > calls || fixture.Time.ActiveTimerCount > 0);
			fixture.Time.Advance(TimeSpan.FromSeconds(11));
		}

		Assert.That(sync.Account.Puts, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_lost_race_is_saved_again_once_the_winning_account_license_is_revoked()
	{
		var fixture = new Fixture();
		var local = await IssueFromProofAsync(fixture, HexId(1));
		var winner = Sign(ProductionKey, ProductionKeyId, HexId(2));
		await using var sync = new SyncHarness(fixture);
		sync.Account.PutAnswer = _ =>
		{
			sync.Account.PutAnswer = null;
			return new PlatformAccountLicenseResult.Conflict(winner, 1);
		};
		sync.Account.Revision = 1;
		sync.Account.AnswerLicenseNullOnFirstGet = true;

		await sync.SignInAsync();
		await Until(() => sync.Account.WaitingPolls > 0);
		sync.Account.RevokeCurrent();
		await Until(() => sync.Account.License == local);

		Assert.That(sync.Account.Puts, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task An_unexpected_answer_backs_off_before_asking_again()
	{
		var fixture = new Fixture();
		await using var sync = new SyncHarness(fixture);
		sync.Account.GetAnswer = new PlatformAccountLicenseResult.Unavailable(null);

		await sync.SignInAsync();
		await Until(() => sync.Account.Calls == 1 && fixture.Time.ActiveTimerCount > 0);
		await Task.Delay(100);
		var beforeBackoff = sync.Account.Calls;
		sync.Account.GetAnswer = null;
		fixture.Time.Advance(CompanionLicenseAccountSyncBackgroundService.InitialRetryDelay);
		await Until(() => sync.Account.Calls > beforeBackoff);

		Assert.That(beforeBackoff, Is.EqualTo(1));
	}

	[Test]
	public async Task An_unchanged_answer_that_came_back_early_waits_before_the_next_poll()
	{
		var fixture = new Fixture();
		await using var sync = new SyncHarness(fixture);
		sync.Account.ImmediatePolls = true;

		await sync.SignInAsync();
		await Until(() => sync.Account.Calls == 2 && fixture.Time.ActiveTimerCount > 0);
		await Task.Delay(100);
		var beforeGap = sync.Account.Calls;
		fixture.Time.Advance(CompanionLicenseAccountSyncBackgroundService.MinimumPollGap - TimeSpan.FromSeconds(1));
		await Task.Delay(100);
		var insideGap = sync.Account.Calls;
		fixture.Time.Advance(TimeSpan.FromSeconds(1) + CompanionLicenseAccountSyncBackgroundService.MaximumPollJitter);
		await Until(() => sync.Account.Calls > insideGap);

		Assert.That(insideGap, Is.EqualTo(beforeGap));
	}

	[Test]
	public async Task A_license_the_account_reports_revoked_is_dropped_at_once()
	{
		var fixture = new Fixture();
		await IssueFromProofAsync(fixture, HexId(1));
		await using var sync = new SyncHarness(fixture);
		sync.Account.PutAnswer = _ => new PlatformAccountLicenseResult.Refused("license-revoked");

		await sync.SignInAsync();
		await Until(async () => !(await fixture.Service.GetStatusAsync(default)).Licensed);

		Assert.That(sync.Account.Puts, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Macro_Deck_says_when_a_device_hands_over_a_license()
	{
		var fixture = new Fixture();

		await fixture.Service.SyncAsync("c",
			new SyncCompanionLicenseRequest { License = Sign(ProductionKey, ProductionKeyId, "license-device") },
			default);

		Assert.That(fixture.Notifications.Snapshot().Select(notification => notification.Title),
			Is.EqualTo(new[] { "Companion license received" }));
	}

	[Test]
	public async Task Macro_Deck_says_in_the_users_language_when_it_downloads_the_account_license()
	{
		var fixture = new Fixture();
		fixture.Preferences.Culture = "de";

		await fixture.Service.ReconcileAccountAsync(Sign(ProductionKey, ProductionKeyId, "license-account"), default);

		Assert.That(fixture.Notifications.Snapshot().Select(notification => notification.Title),
			Is.EqualTo(new[] { "Companion-Lizenz heruntergeladen" }));
	}

	[Test]
	public async Task A_license_already_held_raises_no_notification()
	{
		var fixture = new Fixture();
		var token = Sign(ProductionKey, ProductionKeyId, "license-device");
		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = token }, default);
		fixture.Notifications.DismissAll();

		await fixture.Service.SyncAsync("c", new SyncCompanionLicenseRequest { License = token }, default);
		await fixture.Service.ReconcileAccountAsync(token, default);

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	private static async Task<string> IssueFromProofAsync(Fixture fixture, string licenseId)
	{
		var token = Sign(ProductionKey, ProductionKeyId, licenseId);
		fixture.Platform.Answer = _ => new PlatformLicenseIssueResult.Issued(token);
		await fixture.Service.SyncAsync("c", GooglePlay($"purchase-{licenseId}"), default);
		await fixture.Service.RunDueWorkAsync(default);
		return token;
	}

	private static Task Until(Func<bool> condition) => Until(() => Task.FromResult(condition()));

	private static async Task Until(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!await condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met in time");
			}

			await Task.Delay(10);
		}
	}

	private sealed class SyncHarness : IAsyncDisposable
	{
		private readonly CompanionLicenseAccountSyncBackgroundService _worker;
		private readonly CancellationTokenSource _stop = new();
		private bool _started;

		public SyncHarness(Fixture fixture)
		{
			_worker = new CompanionLicenseAccountSyncBackgroundService(Session,
				fixture.Service,
				Account,
				fixture.Time,
				() => 0,
				new LoggerConfiguration().CreateLogger());
		}

		public FakeConnectSessionService Session { get; } = new();
		public ScriptedAccount Account { get; } = new();

		public async Task StartAsync()
		{
			_started = true;
			await _worker.StartAsync(_stop.Token);
		}

		public async Task SignInAsync()
		{
			Session.Current = FakeConnectSessionService.SignedIn(null);
			Account.Session = Session;
			await StartAsync();
		}

		public async ValueTask DisposeAsync()
		{
			if (_started)
			{
				await _stop.CancelAsync();
				await _worker.StopAsync(CancellationToken.None);
			}

			_worker.Dispose();
			_stop.Dispose();
		}
	}

	private sealed class RacingSession(int signOutOnRead) : IConnectSessionService
	{
		private ConnectSessionSnapshot _current = FakeConnectSessionService.SignedIn(null);
		private int _reads;

		public int Reads => Volatile.Read(ref _reads);

		public ConnectSessionSnapshot Current
		{
			get
			{
				var snapshot = _current;
				if (Interlocked.Increment(ref _reads) == signOutOnRead)
				{
					Publish(ConnectSessionSnapshot.SignedOut);
				}

				return snapshot;
			}
		}

		public event EventHandler<ConnectSessionSnapshot>? SessionChanged;

		public void Publish(ConnectSessionSnapshot snapshot)
		{
			_current = snapshot;
			SessionChanged?.Invoke(this, snapshot);
		}

		public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ConnectSignInStart> StartSignIn(CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task CancelSignIn(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SignOut(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<string> GetAccessToken(CancellationToken cancellationToken = default) => Task.FromResult("access-token");
	}

	internal sealed class ScriptedAccount : IPlatformLicenseAccountClient
	{
		private readonly Lock _lock = new();
		private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _waiting;
		private int _calls;
		private bool _revoked;

		public FakeConnectSessionService? Session { get; set; }
		public string? License { get; private set; }
		public long Revision { get; set; }
		public ConcurrentQueue<string> PutLog { get; } = new();
		public IReadOnlyList<string> Puts => PutLog.ToList();
		public int WaitingPolls => Volatile.Read(ref _waiting);
		public int Calls => Volatile.Read(ref _calls);
		public bool ImmediatePolls { get; set; }
		public bool AnswerLicenseNullOnFirstGet { get; set; }
		public PlatformAccountLicenseResult? GetAnswer { get; set; }
		public Func<string, PlatformAccountLicenseResult>? PutAnswer { get; set; }

		public void Store(string license)
		{
			lock (_lock)
			{
				License = license;
				_revoked = false;
				Revision++;
				Signal();
			}
		}

		public void RevokeCurrent()
		{
			lock (_lock)
			{
				_revoked = true;
				Signal();
			}
		}

		public async Task<PlatformAccountLicenseResult> GetAccountLicenseAsync(long? afterRevision,
			TimeSpan wait,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			if (GetAnswer is { } answer)
			{
				return answer;
			}

			Task changed;
			lock (_lock)
			{
				if (AnswerLicenseNullOnFirstGet)
				{
					AnswerLicenseNullOnFirstGet = false;
					return new PlatformAccountLicenseResult.Current(null, 0);
				}

				if (afterRevision is null || afterRevision != Revision || ImmediatePolls)
				{
					return State();
				}

				changed = _changed.Task;
			}

			Interlocked.Increment(ref _waiting);
			try
			{
				await changed.WaitAsync(cancellationToken);
			}
			finally
			{
				Interlocked.Decrement(ref _waiting);
			}

			lock (_lock)
			{
				return State();
			}
		}

		public Task<PlatformAccountLicenseResult> StoreAccountLicenseAsync(string license,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			PutLog.Enqueue(license);
			if (PutAnswer is { } answer)
			{
				return Task.FromResult(answer(license));
			}

			lock (_lock)
			{
				if (License is null || _revoked)
				{
					License = license;
					_revoked = false;
					Revision++;
					Signal();
					return Task.FromResult<PlatformAccountLicenseResult>(State());
				}

				return Task.FromResult<PlatformAccountLicenseResult>(License == license
					? State()
					: new PlatformAccountLicenseResult.Conflict(License, Revision));
			}
		}

		private PlatformAccountLicenseResult.Current State() => new(_revoked ? null : License, Revision);

		private void Signal()
		{
			_changed.TrySetResult();
			_changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}
}

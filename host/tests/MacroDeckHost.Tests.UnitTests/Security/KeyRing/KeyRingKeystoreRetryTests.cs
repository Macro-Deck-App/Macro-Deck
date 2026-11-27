using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

[TestFixture]
public class KeyRingKeystoreRetryTests
{
	private static readonly KekStoreIdentity _identity = new("Macro Deck Tests", "key-ring-kek");
	private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _shortWait = TimeSpan.FromMilliseconds(100);

	private TestPaths _paths = null!;
	private FakeKekStore _store = null!;
	private ManualResetEventSlim _readGate = null!;
	private byte[] _kek = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.KeysDirectory);
		_store = new FakeKekStore();
		_readGate = new ManualResetEventSlim(false);
		_kek = KeyRingKeyDerivation.CreateKek();
	}

	[TearDown]
	public void TearDown()
	{
		_readGate.Set();
		_readGate.Dispose();
		if (Directory.Exists(_paths.BaseDirectory))
		{
			Directory.Delete(_paths.BaseDirectory, true);
		}
	}

	[Test]
	public async Task A_keystore_that_still_cannot_answer_leaves_everything_as_it_was()
	{
		_store.ReadOverride = KekStoreStatus.Unavailable;
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable);

		var outcome = await keyRing.RetryKeystore();

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(KeyRingKeystoreRetryOutcome.Unavailable));
			Assert.That(_store.Writes, Is.Zero);
			Assert.That(Directory.GetFileSystemEntries(_paths.KeysDirectory), Is.Empty);
			Assert.That(keyRing.Status.State, Is.EqualTo(KeyRingProtectionState.Locked));
		});
	}

	[Test]
	public async Task A_keystore_that_answers_again_with_the_rings_key_is_recovered()
	{
		_store.ReadOverride = KekStoreStatus.Unavailable;
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable);
		var first = await keyRing.RetryKeystore();

		_store.ReadOverride = null;
		_store.Seed(_kek);
		var second = await keyRing.RetryKeystore();

		Assert.That((first, second),
			Is.EqualTo((KeyRingKeystoreRetryOutcome.Unavailable, KeyRingKeystoreRetryOutcome.Recovered)));
	}

	[Test]
	public async Task A_keystore_that_answers_without_the_entry_reports_it_missing()
	{
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable);

		Assert.That(await keyRing.RetryKeystore(), Is.EqualTo(KeyRingKeystoreRetryOutcome.EntryMissing));
	}

	[Test]
	public async Task A_keystore_that_answers_with_another_key_reports_it_stale()
	{
		_store.Seed(KeyRingKeyDerivation.CreateKek());
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable);

		Assert.That(await keyRing.RetryKeystore(), Is.EqualTo(KeyRingKeystoreRetryOutcome.EntryStale));
	}

	[Test]
	public async Task A_keystore_waiting_on_its_prompt_is_pending_and_is_not_asked_a_second_time()
	{
		_store.ReadGate = _readGate;
		_store.Seed(_kek);
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable, null, _shortWait);

		var first = await keyRing.RetryKeystore();
		var second = await keyRing.RetryKeystore();
		_readGate.Set();
		var third = await Until(keyRing, KeyRingKeystoreRetryOutcome.Recovered);

		Assert.Multiple(() =>
		{
			Assert.That((first, second),
				Is.EqualTo((KeyRingKeystoreRetryOutcome.Pending, KeyRingKeystoreRetryOutcome.Pending)));
			Assert.That(third, Is.EqualTo(KeyRingKeystoreRetryOutcome.Recovered));
			Assert.That(_store.Reads, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_read_startup_is_still_waiting_on_is_reused_rather_than_issued_again()
	{
		var startupRead = new TaskCompletionSource<KekStoreReadResult>();
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable,
			KeyRingKeystoreRead.Evaluate(startupRead.Task, KeyRingKeyDerivation.DeriveKekId(_kek)),
			_shortWait);

		var first = await keyRing.RetryKeystore();
		startupRead.SetResult(KekStoreReadResult.Found(_kek.ToArray()));
		var second = await keyRing.RetryKeystore();

		Assert.Multiple(() =>
		{
			Assert.That((first, second),
				Is.EqualTo((KeyRingKeystoreRetryOutcome.Pending, KeyRingKeystoreRetryOutcome.Recovered)));
			Assert.That(_store.Reads, Is.Zero);
		});
	}

	[Test]
	public async Task A_startup_read_that_failed_before_anyone_asked_is_not_served_as_the_answer()
	{
		_store.Seed(_kek);
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable,
			Task.FromResult(new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.Unavailable, "dismissed")));

		Assert.That(await keyRing.RetryKeystore(), Is.EqualTo(KeyRingKeystoreRetryOutcome.Recovered));
	}

	[Test]
	public async Task A_keystore_left_locked_while_waiting_is_reported_once_without_asking_again()
	{
		var startupRead = new TaskCompletionSource<KekStoreReadResult>();
		_store.Seed(_kek);
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable,
			KeyRingKeystoreRead.Evaluate(startupRead.Task, KeyRingKeyDerivation.DeriveKekId(_kek)),
			_shortWait);

		var waiting = await keyRing.RetryKeystore();
		startupRead.SetResult(KekStoreReadResult.Unavailable("The collection is still locked"));
		var dismissed = await keyRing.RetryKeystore();

		Assert.Multiple(() =>
		{
			Assert.That((waiting, dismissed),
				Is.EqualTo((KeyRingKeystoreRetryOutcome.Pending, KeyRingKeystoreRetryOutcome.Unavailable)));
			Assert.That(_store.Reads, Is.Zero);
		});
	}

	[TestCase(KeyRingLockReason.KeystoreEntryMissing)]
	[TestCase(KeyRingLockReason.KeystoreEntryStale)]
	[TestCase(KeyRingLockReason.EscrowMissing)]
	public async Task A_lock_the_keystore_did_not_cause_is_not_retried(KeyRingLockReason reason)
	{
		_store.Seed(_kek);
		using var keyRing = Locked(reason);

		var outcome = await keyRing.RetryKeystore();

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(KeyRingKeystoreRetryOutcome.NotApplicable));
			Assert.That(_store.Reads, Is.Zero);
		});
	}

	[Test]
	public async Task A_host_that_is_not_locked_is_not_retried()
	{
		using var keyRing = Service(Plan(KeyRingProtectionMode.Unprotected, KeyRingLockReason.None));

		Assert.That(await keyRing.RetryKeystore(), Is.EqualTo(KeyRingKeystoreRetryOutcome.NotApplicable));
	}

	[Test]
	public async Task Two_observers_of_one_late_read_both_see_the_rings_key_recovered()
	{
		var late = new TaskCompletionSource<KekStoreReadResult>();
		var found = KekStoreReadResult.Found(_kek.ToArray());
		var outcome = KeyRingKeystoreRead.Evaluate(late.Task, KeyRingKeyDerivation.DeriveKekId(_kek));
		using var keyRing = Locked(KeyRingLockReason.KeystoreUnavailable, outcome, _shortWait);

		var pending = await keyRing.RetryKeystore();
		late.SetResult(found);
		var other = await outcome.WaitAsync(_wait);
		var retried = await keyRing.RetryKeystore();

		Assert.Multiple(() =>
		{
			Assert.That(pending, Is.EqualTo(KeyRingKeystoreRetryOutcome.Pending));
			Assert.That(other.Outcome, Is.EqualTo(KeyRingKeystoreRetryOutcome.Recovered));
			Assert.That(retried, Is.EqualTo(KeyRingKeystoreRetryOutcome.Recovered));
			Assert.That(found.Kek, Is.All.Zero);
		});
	}

	[Test]
	public async Task A_read_that_throws_is_an_unreachable_keystore_with_the_error_as_reason()
	{
		var outcome = await KeyRingKeystoreRead.Evaluate(
			Task.FromException<KekStoreReadResult>(new InvalidOperationException("libsecret crashed")),
			"ring");

		Assert.That(outcome,
			Is.EqualTo(new KeyRingKeystoreReadOutcome(KeyRingKeystoreRetryOutcome.Unavailable, "libsecret crashed")));
	}

	private static async Task<KeyRingKeystoreRetryOutcome> Until(
		KeyRingProtectionService keyRing,
		KeyRingKeystoreRetryOutcome expected)
	{
		using var timeout = new CancellationTokenSource(_wait);
		var outcome = await keyRing.RetryKeystore(timeout.Token);
		while (outcome != expected && outcome == KeyRingKeystoreRetryOutcome.Pending)
		{
			outcome = await keyRing.RetryKeystore(timeout.Token);
		}

		return outcome;
	}

	private KeyRingProtectionService Locked(
		KeyRingLockReason reason,
		Task<KeyRingKeystoreReadOutcome>? pendingRead = null,
		TimeSpan? retryWait = null)
	{
		return Service(Plan(KeyRingProtectionMode.Locked, reason), pendingRead, retryWait);
	}

	private KeyRingProtectionService Service(
		KeyRingProtectionPlan plan,
		Task<KeyRingKeystoreReadOutcome>? pendingRead = null,
		TimeSpan? retryWait = null)
	{
		return new KeyRingProtectionService(_paths,
			_store,
			_identity,
			new KeyRingKekHolder(),
			plan,
			false,
			TimeProvider.System,
			Log.Logger,
			pendingRead,
			retryWait ?? _wait);
	}

	private KeyRingProtectionPlan Plan(KeyRingProtectionMode mode, KeyRingLockReason reason)
	{
		return new KeyRingProtectionPlan(mode,
			reason,
			KeyRingUnprotectedReason.None,
			_store.Backend,
			true,
			null,
			null,
			mode == KeyRingProtectionMode.Locked ? KeyRingKeyDerivation.DeriveKekId(_kek) : null,
			false);
	}
}

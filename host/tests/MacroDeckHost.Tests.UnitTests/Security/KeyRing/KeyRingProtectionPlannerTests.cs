using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

/// <summary>
/// The planner decides, before anything is decrypted, whether the host may use the ring, must leave it
/// alone, or should carry on as before. Getting Unavailable wrong is the expensive case: Data Protection
/// answers a ring it cannot read by minting a replacement, so a backend that merely failed to respond
/// must never be read as "no entry".
/// </summary>
[TestFixture]
public class KeyRingProtectionPlannerTests
{
	private static readonly KekStoreIdentity Identity = new("Macro Deck Tests", "key-ring-kek");

	private string _keysDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_keysDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"), "keys");
		Directory.CreateDirectory(_keysDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		var root = Directory.GetParent(_keysDirectory)!.FullName;
		if (Directory.Exists(root))
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void A_fresh_installation_is_unprotected_and_says_the_recovery_key_was_never_exported()
	{
		var plan = Resolve(new FakeKekStore(), recoveryKeyExported: false);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Unprotected));
			Assert.That(plan.UnprotectedReason, Is.EqualTo(KeyRingUnprotectedReason.RecoveryKeyNotExported));
		});
	}

	[Test]
	public void A_portable_installation_is_unprotected_even_with_a_keystore_and_an_exported_key()
	{
		var plan = Resolve(new FakeKekStore(), recoveryKeyExported: true, portable: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Unprotected));
			Assert.That(plan.UnprotectedReason, Is.EqualTo(KeyRingUnprotectedReason.Portable));
		});
	}

	[Test]
	public void Without_a_keystore_the_ring_stays_unprotected_and_the_reason_is_reported()
	{
		var store = new FakeKekStore { Availability = new KekStoreAvailability(false, "libsecret is not installed") };

		var plan = Resolve(store, recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Unprotected));
			Assert.That(plan.UnprotectedReason, Is.EqualTo(KeyRingUnprotectedReason.NoKeystore));
			Assert.That(plan.BackendAvailable, Is.False);
			Assert.That(plan.BackendUnavailableReason, Is.EqualTo("libsecret is not installed"));
		});
	}

	[Test]
	public void A_wrapped_ring_with_its_key_present_is_protected()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		WrapRing(kek);
		var store = new FakeKekStore();
		store.Seed(kek);

		var plan = Resolve(store, recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Protected));
			Assert.That(plan.Kek, Is.EqualTo(kek));
		});
	}

	[Test]
	public void A_wrapped_ring_whose_key_is_gone_locks_and_points_at_the_escrow()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		WrapRing(kek);
		WriteEscrow(kek);

		var plan = Resolve(new FakeKekStore(), recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Locked));
			Assert.That(plan.LockReason, Is.EqualTo(KeyRingLockReason.KeystoreEntryMissing));
		});
	}

	// Nothing can recover the ring in this state, so the gate has to say so rather than ask for a key
	// that will never work.
	[Test]
	public void A_wrapped_ring_with_no_escrow_locks_with_a_different_reason()
	{
		WrapRing(KeyRingKeyDerivation.CreateKek());

		var plan = Resolve(new FakeKekStore(), recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Locked));
			Assert.That(plan.LockReason, Is.EqualTo(KeyRingLockReason.EscrowMissing));
		});
	}

	[Test]
	public void A_keystore_holding_a_different_key_locks_rather_than_pretending_to_be_protected()
	{
		WrapRing(KeyRingKeyDerivation.CreateKek());
		WriteEscrow(KeyRingKeyDerivation.CreateKek());
		var store = new FakeKekStore();
		store.Seed(KeyRingKeyDerivation.CreateKek());

		var plan = Resolve(store, recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Locked));
			Assert.That(plan.LockReason, Is.EqualTo(KeyRingLockReason.KeystoreEntryStale));
		});
	}

	// The dangerous one: an unreachable backend read as "no entry" would let the host carry on and
	// replace a ring that was never actually broken.
	[Test]
	public void An_unreachable_backend_locks_a_wrapped_ring_rather_than_treating_it_as_unprotected()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		WrapRing(kek);
		WriteEscrow(kek);
		var store = new FakeKekStore
		{
			ReadOverride = KekStoreStatus.Unavailable,
			Availability = new KekStoreAvailability(false, "the keychain is locked")
		};

		var plan = Resolve(store, recoveryKeyExported: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Mode, Is.EqualTo(KeyRingProtectionMode.Locked));
			Assert.That(plan.LockReason, Is.EqualTo(KeyRingLockReason.KeystoreUnavailable));
			Assert.That(plan.Kek, Is.Null);
		});
	}

	private KeyRingProtectionPlan Resolve(FakeKekStore store, bool recoveryKeyExported, bool portable = false)
		=> KeyRingProtectionPlanner.Resolve(_keysDirectory, store, Identity, recoveryKeyExported, portable);

	private void WrapRing(byte[] kek)
	{
		var services = new ServiceCollection();
		var builder = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(_keysDirectory))
			.SetApplicationName("MacroDeck");
		var holder = new KeyRingKekHolder();
		KeyRingDataProtection.Configure(builder, holder, KeyRingProtectionMode.Unprotected);

		using (var provider = services.BuildServiceProvider())
		{
			provider.GetRequiredService<IDataProtectionProvider>()
				.CreateProtector("MacroDeck.Secrets")
				.Protect(Encoding.UTF8.GetBytes("seed"));
		}

		holder.Set(kek);
		new KeyRingMigrator(Log.Logger).Migrate(_keysDirectory,
			new KeyRingXmlEncryptor(holder),
			new KeyRingXmlDecryptor(holder));
	}

	private void WriteEscrow(byte[] kek)
		=> new KekEscrowStore(Log.Logger, TimeProvider.System).AddWrap(_keysDirectory,
			kek,
			RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes));
}

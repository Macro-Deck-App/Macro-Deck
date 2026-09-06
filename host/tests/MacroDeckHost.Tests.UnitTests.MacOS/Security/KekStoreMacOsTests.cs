using System.Runtime.Versioning;
using System.Security.Cryptography;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Security;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class KekStoreMacOsTests
{
	private MacOsKeychainKekStore _store = null!;
	private KekStoreIdentity _identity = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new MacOsKeychainKekStore();

		// A per-run service name: the fixture writes to the real login keychain, so it must never be
		// able to touch the entry an installed Macro Deck depends on.
		_identity = new KekStoreIdentity($"Macro Deck Tests {Guid.NewGuid():N}", "key-ring-kek");
	}

	[TearDown]
	public void TearDown() => _store.Delete(_identity);

	[Test]
	public void The_factory_returns_the_keychain_store_on_this_platform()
	{
		var store = KekStoreFactory.Create();

		Assert.That(store.Backend, Is.EqualTo(KeyRingBackend.MacOsKeychain));
	}

	[Test]
	public void A_written_key_reads_back_byte_for_byte_and_is_gone_after_delete()
	{
		if (!_store.Availability.Supported)
		{
			Assert.Ignore($"No usable keychain on this machine: {_store.Availability.Reason}");
		}

		var kek = RandomNumberGenerator.GetBytes(32);

		Assert.That(_store.Write(_identity, kek), Is.EqualTo(KekStoreStatus.Found));

		var read = _store.Read(_identity);

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(KekStoreStatus.Found));
			Assert.That(read.Kek, Is.EqualTo(kek));
		});

		Assert.That(_store.Delete(_identity), Is.EqualTo(KekStoreStatus.Found));
		Assert.That(_store.Read(_identity).Status, Is.EqualTo(KekStoreStatus.NotFound));
	}

	[Test]
	public void Writing_twice_replaces_the_key_rather_than_failing_on_a_duplicate()
	{
		if (!_store.Availability.Supported)
		{
			Assert.Ignore($"No usable keychain on this machine: {_store.Availability.Reason}");
		}

		var first = RandomNumberGenerator.GetBytes(32);
		var second = RandomNumberGenerator.GetBytes(32);

		_store.Write(_identity, first);

		Assert.That(_store.Write(_identity, second), Is.EqualTo(KekStoreStatus.Found));
		Assert.That(_store.Read(_identity).Kek, Is.EqualTo(second));
	}

	// An absent entry is what sends the host to the escrow for recovery, while an unavailable backend
	// must leave everything alone. Reporting the first as the second would strand every secret.
	[Test]
	public void An_absent_entry_reports_not_found_rather_than_unavailable()
	{
		if (!_store.Availability.Supported)
		{
			Assert.Ignore($"No usable keychain on this machine: {_store.Availability.Reason}");
		}

		var read = _store.Read(_identity);

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(KekStoreStatus.NotFound));
			Assert.That(read.Kek, Is.Null);
		});
	}
}

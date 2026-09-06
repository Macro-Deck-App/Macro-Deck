using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

[TestFixture]
public class KekEscrowStoreTests
{
	private string _keysDirectory = null!;
	private KekEscrowStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_keysDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"), "keys");
		Directory.CreateDirectory(_keysDirectory);
		_store = new KekEscrowStore(Log.Logger, TimeProvider.System);
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
	public void A_wrapped_key_comes_back_byte_for_byte_with_the_recovery_key_that_wrapped_it()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var recoveryKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		_store.AddWrap(_keysDirectory, kek, recoveryKey);
		var (result, recovered) = _store.TryOpen(_keysDirectory, recoveryKey);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(KekEscrowResult.Ok));
			Assert.That(recovered, Is.EqualTo(kek));
		});
	}

	[Test]
	public void A_different_recovery_key_opens_nothing()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		_store.AddWrap(_keysDirectory,
			kek,
			RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes));

		var (result, recovered) = _store.TryOpen(_keysDirectory,
			RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(KekEscrowResult.NoMatchingWrap));
			Assert.That(recovered, Is.Null);
		});
	}

	// Regenerating a recovery key blanks ExportedAt, so between the rotation and the moment the user
	// saves the replacement there are two keys either of which they might be holding. Dropping the old
	// wrap in that window is what turns a lost keystore entry into a lost installation.
	[Test]
	public void Both_the_old_and_the_new_recovery_key_open_the_escrow_until_it_is_pruned()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var oldKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var newKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		_store.AddWrap(_keysDirectory, kek, oldKey);
		_store.AddWrap(_keysDirectory, kek, newKey);

		Assert.Multiple(() =>
		{
			Assert.That(_store.TryOpen(_keysDirectory, oldKey).Kek, Is.EqualTo(kek));
			Assert.That(_store.TryOpen(_keysDirectory, newKey).Kek, Is.EqualTo(kek));
			Assert.That(_store.Read(_keysDirectory)!.Wraps, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void Pruning_keeps_the_named_key_working_and_stops_the_superseded_one()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var oldKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		var newKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		_store.AddWrap(_keysDirectory, kek, oldKey);
		_store.AddWrap(_keysDirectory, kek, newKey);

		var result = _store.PruneExcept(_keysDirectory, BackupKeyDerivation.DeriveKeyId(newKey));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(KekEscrowResult.Ok));
			Assert.That(_store.TryOpen(_keysDirectory, newKey).Kek, Is.EqualTo(kek));
			Assert.That(_store.TryOpen(_keysDirectory, oldKey).Result, Is.EqualTo(KekEscrowResult.NoMatchingWrap));
		});
	}

	// An escrow with no wraps recovers nothing, so a prune that would produce one is a bug rather than
	// an outcome - and it would only be discovered on the day someone actually needed to recover.
	[Test]
	public void Pruning_to_a_key_that_has_no_wrap_is_refused_rather_than_emptying_the_escrow()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var wrapped = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		_store.AddWrap(_keysDirectory, kek, wrapped);
		var stranger = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		var result = _store.PruneExcept(_keysDirectory, BackupKeyDerivation.DeriveKeyId(stranger));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(KekEscrowResult.WouldEmpty));
			Assert.That(_store.TryOpen(_keysDirectory, wrapped).Kek, Is.EqualTo(kek));
		});
	}

	[Test]
	public void Wrapping_the_same_recovery_key_twice_leaves_one_wrap_that_still_opens()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var recoveryKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);

		_store.AddWrap(_keysDirectory, kek, recoveryKey);
		_store.AddWrap(_keysDirectory, kek, recoveryKey);

		Assert.Multiple(() =>
		{
			Assert.That(_store.Read(_keysDirectory)!.Wraps, Has.Count.EqualTo(1));
			Assert.That(_store.TryOpen(_keysDirectory, recoveryKey).Kek, Is.EqualTo(kek));
		});
	}

	[Test]
	public void A_missing_escrow_is_reported_as_missing_rather_than_as_a_wrong_key()
	{
		var (result, recovered) = _store.TryOpen(_keysDirectory,
			RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes));

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(KekEscrowResult.Missing));
			Assert.That(recovered, Is.Null);
		});
	}

	[Test]
	public void An_escrow_written_by_a_newer_version_is_refused_rather_than_half_read()
	{
		var kek = KeyRingKeyDerivation.CreateKek();
		var recoveryKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		_store.AddWrap(_keysDirectory, kek, recoveryKey);

		BumpVersion(KekEscrowStore.PathFor(_keysDirectory));

		Assert.That(_store.TryOpen(_keysDirectory, recoveryKey).Result, Is.EqualTo(KekEscrowResult.Unreadable));
	}

	[Test]
	public void A_wrap_moved_from_another_escrow_does_not_open()
	{
		var recoveryKey = RandomNumberGenerator.GetBytes(BackupKeyDerivation.RecoveryKeyBytes);
		_store.AddWrap(_keysDirectory, KeyRingKeyDerivation.CreateKek(), recoveryKey);
		var foreign = _store.Read(_keysDirectory)!.Wraps.Single();

		var other = Path.Combine(Directory.GetParent(_keysDirectory)!.FullName, "other-keys");
		Directory.CreateDirectory(other);
		_store.AddWrap(other, KeyRingKeyDerivation.CreateKek(), recoveryKey);
		var document = _store.Read(other)!;
		document.Wraps.Clear();
		document.Wraps.Add(foreign);
		File.WriteAllText(KekEscrowStore.PathFor(other),
			JsonSerializer.Serialize(document));

		Assert.That(_store.TryOpen(other, recoveryKey).Result, Is.EqualTo(KekEscrowResult.NoMatchingWrap));
	}

	private static void BumpVersion(string path)
	{
		var bytes = File.ReadAllBytes(path);
		var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

		var node = JsonNode.Parse(bytes.AsSpan(bom).ToArray())!.AsObject();
		var versionProperty = node.Select(pair => pair.Key)
			.First(key => key.Equals("version", StringComparison.OrdinalIgnoreCase));
		node[versionProperty] = KekEscrowDocument.CurrentVersion + 1;

		File.WriteAllBytes(path,
			[.. bytes.Take(bom), .. Encoding.UTF8.GetBytes(node.ToJsonString())]);
	}
}

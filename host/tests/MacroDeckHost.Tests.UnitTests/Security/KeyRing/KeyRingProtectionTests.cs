using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

/// <summary>
/// Access is always proven by reading a value back, never by inspecting the key files: an
/// implementation that reports success while orphaning every secret passes any state-only assertion.
/// </summary>
[TestFixture]
public class KeyRingProtectionTests
{
	private const string Marker = "3f9a7c21-secret-marker-a1b2c3d4e5f6";

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
	public void A_migrated_key_ring_still_unprotects_what_it_protected_before()
	{
		var protectedPayload = ProtectWithUnprotectedRing(Marker);
		var kek = KeyRingKeyDerivation.CreateKek();

		Migrate(kek);

		Assert.That(Unprotect(protectedPayload, kek), Is.EqualTo(Marker));
	}

	[Test]
	public void A_copy_of_the_keys_directory_is_useless_without_the_key_encryption_key()
	{
		var protectedPayload = ProtectWithUnprotectedRing(Marker);
		var kek = KeyRingKeyDerivation.CreateKek();

		Migrate(kek);

		// Exactly what someone holding a Time Machine copy has: the files, and no keystore.
		Assert.Throws<CryptographicException>(() => Unprotect(protectedPayload, kek: null));
	}

	// A wrap under a constant, a machine id, or anything else an attacker can recompute would pass the
	// test above, because the copy has no key either way. Two installations failing to open each
	// other's rings is what proves the key is actually per-installation and random.
	[Test]
	public void Two_installations_cannot_open_each_others_key_rings()
	{
		var protectedPayload = ProtectWithUnprotectedRing(Marker);
		var mine = KeyRingKeyDerivation.CreateKek();
		var theirs = KeyRingKeyDerivation.CreateKek();

		Migrate(mine);

		Assert.Multiple(() =>
		{
			Assert.That(mine, Is.Not.EqualTo(theirs));
			Assert.That(mine, Has.Length.EqualTo(KeyRingKeyDerivation.KekBytes));
			Assert.Throws<CryptographicException>(() => Unprotect(protectedPayload, theirs));
		});
	}

	[Test]
	public void The_plaintext_master_key_survives_nowhere_in_the_keys_directory()
	{
		ProtectWithUnprotectedRing(Marker);
		var masterKeys = ReadMasterKeyValues();
		Assert.That(masterKeys, Is.Not.Empty, "the unprotected ring should expose its master key in the clear");

		Migrate(KeyRingKeyDerivation.CreateKek());

		var survivors = Directory.EnumerateFiles(_keysDirectory, "*", SearchOption.AllDirectories)
			.Where(path => masterKeys.Any(key => File.ReadAllText(path).Contains(key, StringComparison.Ordinal)))
			.ToList();

		Assert.That(survivors, Is.Empty, "plaintext key material was left behind");
	}

	[Test]
	public void Migrating_an_already_protected_ring_leaves_it_alone()
	{
		var protectedPayload = ProtectWithUnprotectedRing(Marker);
		var kek = KeyRingKeyDerivation.CreateKek();
		Migrate(kek);
		var afterFirst = ReadAllKeyFiles();

		var outcome = Migrate(kek);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Wrapped, Is.Zero);
			Assert.That(outcome.AlreadyWrapped, Is.GreaterThan(0));
			Assert.That(ReadAllKeyFiles(), Is.EqualTo(afterFirst));
			Assert.That(Unprotect(protectedPayload, kek), Is.EqualTo(Marker));
		});
	}

	// Data Protection captures its encryptor when the key manager is built, so a key created after the
	// ring was wrapped is the case a one-shot migration pass silently misses.
	[Test]
	public void A_key_created_after_the_ring_was_wrapped_is_wrapped_too()
	{
		ProtectWithUnprotectedRing(Marker);
		var kek = KeyRingKeyDerivation.CreateKek();
		Migrate(kek);
		var before = Directory.GetFiles(_keysDirectory, KeyRingFileInspector.KeyFilePattern).Length;

		using var services = BuildProvider(kek, KeyRingProtectionMode.Protected);
		var keyManager = services.GetRequiredService<IKeyManager>();
		keyManager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));

		var states = KeyRingFileInspector.InspectDirectory(_keysDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(states, Has.Count.EqualTo(before + 1), "the test did not actually rotate");
			Assert.That(states.Select(state => state.Protection),
				Is.All.EqualTo(KeyRingFileProtection.KeyEncryptionKey));
		});
	}

	[Test]
	public void The_decryptor_type_name_is_the_one_written_into_key_files()
	{
		// Data Protection resolves this name out of every key file it wrote. Renaming or moving the
		// type would make existing installations unreadable, so the constant and the type must agree.
		Assert.That(typeof(KeyRingXmlDecryptor).FullName, Is.EqualTo(KeyRingXmlNames.DecryptorTypeName));
	}

	[Test]
	public void Encrypting_does_not_modify_the_element_it_was_given()
	{
		var holder = new KeyRingKekHolder();
		holder.Set(KeyRingKeyDerivation.CreateKek());
		var element = new XElement("masterKey", new XElement("value", "abc"));
		var before = element.ToString();

		new KeyRingXmlEncryptor(holder).Encrypt(element);

		Assert.That(element.ToString(), Is.EqualTo(before));
	}

	[Test]
	public void A_tampered_key_file_fails_authentication_rather_than_decrypting_to_something_else()
	{
		ProtectWithUnprotectedRing(Marker);
		var kek = KeyRingKeyDerivation.CreateKek();
		Migrate(kek);

		var path = Directory.GetFiles(_keysDirectory, KeyRingFileInspector.KeyFilePattern).Single();
		var document = XDocument.Load(path);
		var secret = document.Descendants(KeyRingXmlNames.SecretElement).First();
		secret.Attribute(KeyRingXmlNames.KekIdAttribute)!.Value = new string('0', 32);
		document.Save(path);

		var holder = new KeyRingKekHolder();
		holder.Set(kek);

		Assert.Throws<CryptographicException>(() =>
			new KeyRingXmlDecryptor(holder).Decrypt(XDocument.Load(path).Descendants(KeyRingXmlNames.SecretElement)
				.First()));
	}

	private byte[] ProtectWithUnprotectedRing(string value)
	{
		using var services = BuildProvider(kek: null, KeyRingProtectionMode.Unprotected);

		return services.GetRequiredService<IDataProtectionProvider>()
			.CreateProtector("MacroDeck.Secrets")
			.Protect(Encoding.UTF8.GetBytes(value));
	}

	private string Unprotect(byte[] payload, byte[]? kek)
	{
		using var services = BuildProvider(kek, KeyRingProtectionMode.Protected);

		return Encoding.UTF8.GetString(services.GetRequiredService<IDataProtectionProvider>()
			.CreateProtector("MacroDeck.Secrets")
			.Unprotect(payload));
	}

	private KeyRingMigrationOutcome Migrate(byte[] kek)
	{
		var holder = new KeyRingKekHolder();
		holder.Set(kek);

		return new KeyRingMigrator(Log.Logger).Migrate(_keysDirectory,
			new KeyRingXmlEncryptor(holder),
			new KeyRingXmlDecryptor(holder));
	}

	private ServiceProvider BuildProvider(byte[]? kek, KeyRingProtectionMode mode)
	{
		var holder = new KeyRingKekHolder();
		if (kek is not null)
		{
			holder.Set(kek);
		}

		var services = new ServiceCollection();
		var builder = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(_keysDirectory))
			.SetApplicationName("MacroDeck");
		KeyRingDataProtection.Configure(builder, holder, mode);

		return services.BuildServiceProvider();
	}

	private List<string> ReadMasterKeyValues()
		=>
		[
			.. Directory.EnumerateFiles(_keysDirectory, KeyRingFileInspector.KeyFilePattern)
				.SelectMany(path => XDocument.Load(path).Descendants()
					.Where(KeyRingFileInspector.RequiresEncryption)
					.Descendants()
					.Select(element => element.Value)
					.Where(value => value.Length > 20))
		];

	private Dictionary<string, string> ReadAllKeyFiles()
		=> Directory.EnumerateFiles(_keysDirectory, KeyRingFileInspector.KeyFilePattern)
			.ToDictionary(path => Path.GetFileName(path), File.ReadAllText, StringComparer.Ordinal);
}

using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Infrastructure.Secrets;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

/// <summary>
/// When the key ring is wrapped, and what the escrow holds afterwards. The recovery key is the only way
/// back into a wrapped ring, so the invariant that matters is that every key the user could plausibly be
/// holding still opens the escrow.
/// </summary>
[TestFixture]
[NonParallelizable]
public class KeyRingWrapTriggerTests
{
	private static readonly KekStoreIdentity Identity = new("Macro Deck Tests", "key-ring-kek");

	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;
	private ServiceProvider _provider = null!;
	private FakeKekStore _store = null!;
	private KeyRingProtectionService _keyRing = null!;
	private KekEscrowStore _escrow = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		_store = new FakeKekStore();
		_escrow = new KekEscrowStore(Log.Logger, TimeProvider.System);

		var plan = KeyRingProtectionPlanner.Resolve(_paths.KeysDirectory,
			_store,
			Identity,
			recoveryKeyExported: false,
			portable: false);
		_keyRing = new KeyRingProtectionService(_paths,
			_store,
			Identity,
			new KeyRingKekHolder(),
			plan,
			portable: false,
			TimeProvider.System,
			Log.Logger);

		var services = new ServiceCollection();
		services.AddSingleton<ILogger>(Log.Logger);
		services.AddSingleton<IMacroDeckPaths>(_paths);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IKeyRingProtectionService>(_keyRing);
		services.AddDbContext<DatabaseContext>();
		services.AddScoped<ISecretRepository, SecretRepository>();
		services.AddScoped<ISecretService, SecretService>();
		services.AddScoped<IAppPreferenceRepository, AppPreferenceRepository>();
		services.AddScoped<IBackupRecoveryKeyService, BackupRecoveryKeyService>();
		services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(_paths.KeysDirectory))
			.SetApplicationName("MacroDeck");

		_provider = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown()
	{
		_provider.Dispose();
		_keyRing.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	// A key minted implicitly by the first backup has never been shown to anyone, so wrapping the ring
	// under it would put the installation one keystore reset away from being unrecoverable.
	[Test]
	public async Task Creating_a_recovery_key_without_exporting_it_leaves_the_ring_untouched()
	{
		await Recovery().EnsureCreated();

		Assert.Multiple(() =>
		{
			Assert.That(KekEscrowStore.Exists(_paths.KeysDirectory), Is.False);
			Assert.That(_store.Writes, Is.Zero);
			Assert.That(_keyRing.Status.State, Is.EqualTo(KeyRingProtectionState.Unprotected));
		});
	}

	[Test]
	public async Task Exporting_the_recovery_key_wraps_the_ring_and_escrows_it_under_that_key()
	{
		var recovery = Recovery();
		await recovery.EnsureCreated();

		var exported = await recovery.Export();
		Assert.That(exported.Success, Is.True);

		var key = Parse(exported.Data);

		Assert.Multiple(() =>
		{
			Assert.That(_keyRing.Status.State, Is.EqualTo(KeyRingProtectionState.Protected));
			Assert.That(_escrow.TryOpen(_paths.KeysDirectory, key).Result, Is.EqualTo(KekEscrowResult.Ok));
			Assert.That(_store.Stored, Is.Not.Null);
		});
	}

	// The window this closes: the replacement has been minted but the user has not written it down yet,
	// so the key they actually hold is still the old one.
	[Test]
	public async Task Regenerating_keeps_the_old_recovery_key_working_until_the_new_one_is_exported()
	{
		var recovery = Recovery();
		await recovery.EnsureCreated();
		var first = await recovery.Export();
		var oldKey = Parse(first.Data);
		var kek = _store.Stored!;

		var regenerated = await recovery.Regenerate();
		Assert.That(regenerated.Success, Is.True, regenerated.ErrorMessage);
		var newKey = Parse(regenerated.Data);

		Assert.Multiple(() =>
		{
			Assert.That(_escrow.TryOpen(_paths.KeysDirectory, oldKey).Kek, Is.EqualTo(kek));
			Assert.That(_escrow.TryOpen(_paths.KeysDirectory, newKey).Kek, Is.EqualTo(kek));
		});

		var second = await recovery.Export();
		var exportedNewKey = Parse(second.Data);

		Assert.Multiple(() =>
		{
			Assert.That(_escrow.TryOpen(_paths.KeysDirectory, exportedNewKey).Kek,
				Is.EqualTo(kek),
				"the exported key must still open the escrow");
			Assert.That(_escrow.TryOpen(_paths.KeysDirectory, oldKey).Result,
				Is.EqualTo(KekEscrowResult.NoMatchingWrap),
				"the superseded key should have been pruned");
		});
	}

	[Test]
	public async Task Unlocking_with_the_exported_recovery_key_restores_the_original_key()
	{
		var recovery = Recovery();
		await recovery.EnsureCreated();
		var exported = await recovery.Export();
		var key = Parse(exported.Data);
		var original = _store.Stored!;

		// A lost entry is discovered by the planner at startup, never mid-session, so the service has to
		// be rebuilt the way the next launch would build it.
		_store.Forget();
		using var restarted = Restart();
		var result = await restarted.Unlock(key);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);

			// A recovery that mints a fresh key would report success, restart cleanly, and have
			// destroyed every secret in the installation.
			Assert.That(_store.Stored, Is.EqualTo(original));
		});
	}

	[Test]
	public async Task Unlocking_with_the_wrong_key_changes_nothing()
	{
		var recovery = Recovery();
		await recovery.EnsureCreated();
		await recovery.Export();
		var escrowBefore = File.ReadAllBytes(KekEscrowStore.PathFor(_paths.KeysDirectory));
		_store.Forget();
		using var restarted = Restart();

		var result = await restarted.Unlock(KeyRingKeyDerivation.CreateKek());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(KeyRingProtectionError.RecoveryKeyInvalid));
			Assert.That(_store.Stored, Is.Null);
			Assert.That(File.ReadAllBytes(KekEscrowStore.PathFor(_paths.KeysDirectory)), Is.EqualTo(escrowBefore));
		});
	}

	// The documented fallback: no keystore means the ring stays exactly as readable as it was before
	// this feature existed, and the installation keeps working. Half-wrapping it would be unrecoverable.
	[Test]
	public async Task Without_a_keystore_the_recovery_key_still_exports_and_nothing_is_wrapped()
	{
		_store.Availability = new KekStoreAvailability(false, "libsecret is not installed");
		var recovery = Recovery();
		await recovery.EnsureCreated();

		var exported = await recovery.Export();

		Assert.Multiple(() =>
		{
			Assert.That(exported.Success, Is.True);
			Assert.That(KekEscrowStore.Exists(_paths.KeysDirectory), Is.False);
			Assert.That(_store.Writes, Is.Zero);
			Assert.That(_keyRing.Status.State, Is.EqualTo(KeyRingProtectionState.Unprotected));
			Assert.That(_keyRing.Status.BackendAvailable, Is.False);
			Assert.That(_keyRing.Status.BackendUnavailableReason, Is.EqualTo("libsecret is not installed"));
		});
	}

	// Data Protection captures its encryptor when the key manager is built, so a key it rotates during
	// the session that first wrapped the ring lands readable. Nothing else notices: the ring already
	// counts as protected, so it has to be finished rather than migrated again.
	[Test]
	public async Task A_key_left_readable_on_a_protected_ring_is_wrapped_on_the_next_start()
	{
		var recovery = Recovery();
		await recovery.EnsureCreated();
		await recovery.Export();

		var stray = Path.Combine(_paths.KeysDirectory, "key-99999999-9999-9999-9999-999999999999.xml");
		File.WriteAllText(stray, await File.ReadAllTextAsync(StrayTemplate()));
		Assert.That(KeyRingFileInspector.InspectFile(stray).Protection,
			Is.EqualTo(KeyRingFileProtection.Plaintext),
			"the fixture did not actually produce a readable key file");

		await _keyRing.RewrapPending();

		Assert.That(KeyRingFileInspector.InspectDirectory(_paths.KeysDirectory).Select(state => state.Protection),
			Is.All.EqualTo(KeyRingFileProtection.KeyEncryptionKey));
	}

	// A plaintext key file in the shape Data Protection writes, produced by a provider with no encryptor.
	private string StrayTemplate()
	{
		var directory = Path.Combine(_dataDir, "stray");
		Directory.CreateDirectory(directory);

		var services = new ServiceCollection();
		services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(directory))
			.SetApplicationName("MacroDeck");

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IDataProtectionProvider>()
			.CreateProtector("MacroDeck.Secrets")
			.Protect([1, 2, 3]);

		return Directory.GetFiles(directory, KeyRingFileInspector.KeyFilePattern).Single();
	}

	/// <summary>Rebuilds the service from the key ring as it is on disk, as the next launch would.</summary>
	private KeyRingProtectionService Restart()
		=> new(_paths,
			_store,
			Identity,
			new KeyRingKekHolder(),
			KeyRingProtectionPlanner.Resolve(_paths.KeysDirectory,
				_store,
				Identity,
				recoveryKeyExported: true,
				portable: false),
			portable: false,
			TimeProvider.System,
			Log.Logger);

	private static byte[] Parse(string? exported)
	{
		Assert.That(BackupRecoveryKeyFormat.TryParseExported(exported, out var key), Is.True);

		return key;
	}

	private IBackupRecoveryKeyService Recovery()
		=> _provider.CreateScope().ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();
}

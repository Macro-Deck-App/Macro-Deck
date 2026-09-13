using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[TestFixture]
public class FileHostIdentityKeyProviderTests
{
	private TestPaths _paths = null!;
	private EphemeralDataProtectionProvider _protection = null!;
	private InMemoryPreferences _preferences = null!;
	private UserNotificationStore _notifications = null!;
	private ManualTimeProvider _time = null!;
	private ErrorCountingSink _log = null!;

	private string KeyPath => Path.Combine(_paths.KeysDirectory, FileHostIdentityKeyProvider.KeyFileName);

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_protection = new EphemeralDataProtectionProvider();
		_preferences = new InMemoryPreferences();
		_notifications = new UserNotificationStore();
		_time = new ManualTimeProvider();
		_log = new ErrorCountingSink();
	}

	[TearDown]
	public void TearDown()
	{
		if (File.Exists(KeyPath) && !OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}

		_paths.Cleanup();
	}

	[Test]
	public async Task The_same_key_comes_back_after_a_restart()
	{
		var first = await NewProvider().GetPublicKey();
		var second = await NewProvider().GetPublicKey();

		Assert.Multiple(() =>
		{
			Assert.That(second, Is.EqualTo(first));
			Assert.That(first, Has.Length.EqualTo(65));
			Assert.That(first[0], Is.EqualTo(0x04));
		});
	}

	[Test]
	public async Task The_first_key_on_an_existing_host_is_created_silently_and_recorded()
	{
		await NewProvider().Sign("message"u8.ToArray());

		await Eventually(() => _preferences.Has("identity.issued"));
		Assert.That(_notifications.Snapshot(), Is.Empty, "devices paired before the upgrade are unpinned");
	}

	[Test]
	public async Task A_key_recreated_after_one_was_issued_tells_the_user_to_pair_again()
	{
		await NewProvider().Sign("message"u8.ToArray());
		await Eventually(() => _preferences.Has("identity.issued"));
		File.Delete(KeyPath);

		await NewProvider().Sign("message"u8.ToArray());

		await Eventually(() => _notifications.Snapshot().Count == 1);
		Assert.That(_notifications.Snapshot().Single().Kind, Is.EqualTo(UserNotificationKind.Security));
	}

	[Test]
	public async Task An_unprotectable_key_is_renewed_and_the_old_file_is_kept_aside()
	{
		await File.WriteAllBytesAsync(KeyPath, [1, 2, 3]);

		var publicKey = await NewProvider().GetPublicKey();
		var reloaded = await NewProvider().GetPublicKey();
		await Eventually(() => _notifications.Snapshot().Count == 1);

		var aside = Directory.GetFiles(_paths.KeysDirectory, "host-identity.key.unreadable-*");
		Assert.Multiple(() =>
		{
			Assert.That(aside, Has.Length.EqualTo(1));
			Assert.That(File.ReadAllBytes(aside[0]), Is.EqualTo(new byte[] { 1, 2, 3 }));
			Assert.That(reloaded, Is.EqualTo(publicKey));
			Assert.That(_notifications.Snapshot().Single().Kind, Is.EqualTo(UserNotificationKind.Security));
		});
	}

	[Test]
	public async Task A_renewal_while_the_database_is_locked_is_available_at_once_and_still_announced()
	{
		await File.WriteAllBytesAsync(KeyPath, [1, 2, 3]);
		_preferences.FailingWrites = int.MaxValue;

		var publicKey = await NewProvider().GetPublicKey();

		await Eventually(() => _notifications.Snapshot().Count == 1);
		Assert.Multiple(() =>
		{
			Assert.That(publicKey, Has.Length.EqualTo(65), "no retry interval without a key");
			Assert.That(_notifications.Snapshot().Single().Kind, Is.EqualTo(UserNotificationKind.Security));
			Assert.That(_log.Errors, Is.Zero);
		});
	}

	[Test]
	public async Task A_flag_write_that_hits_a_locked_database_once_is_retried()
	{
		await File.WriteAllBytesAsync(KeyPath, [1, 2, 3]);
		_preferences.FailingWrites = 1;

		await NewProvider().GetPublicKey();

		await Eventually(() => _preferences.Has("identity.issued"));
		Assert.That(_notifications.Snapshot(), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_first_key_whose_flag_cannot_be_read_is_announced_rather_than_silent()
	{
		_preferences.FailingReads = int.MaxValue;

		var publicKey = await NewProvider().GetPublicKey();

		Assert.That(publicKey, Has.Length.EqualTo(65));
		await Eventually(() => _notifications.Snapshot().Count == 1);
	}

	[Test]
	public async Task A_key_on_another_curve_is_treated_as_unreadable()
	{
		using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
		var protector = _protection.CreateProtector("MacroDeck.Auth.HostIdentityKey");
		await File.WriteAllBytesAsync(KeyPath, protector.Protect(p384.ExportPkcs8PrivateKey()));

		var publicKey = await NewProvider().GetPublicKey();

		Assert.Multiple(() =>
		{
			Assert.That(publicKey, Has.Length.EqualTo(65));
			Assert.That(Directory.GetFiles(_paths.KeysDirectory, "host-identity.key.unreadable-*"),
				Has.Length.EqualTo(1));
		});
	}

	[Test]
	public async Task An_access_error_is_never_renewed_retried_at_most_every_interval_and_logged_once()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Ignore("Unix file modes simulate the access error.");
			return;
		}

		var original = await NewProvider().GetPublicKey();
		File.SetUnixFileMode(KeyPath, UnixFileMode.None);
		var provider = NewProvider();

		Assert.ThrowsAsync<HostIdentityUnavailableException>(async () => await provider.GetPublicKey());
		_time.Advance(FileHostIdentityKeyProvider.RetryInterval);
		Assert.ThrowsAsync<HostIdentityUnavailableException>(async () => await provider.GetPublicKey());

		File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		Assert.ThrowsAsync<HostIdentityUnavailableException>(async () => await provider.GetPublicKey(),
			"no new attempt before the interval has passed");

		Assert.Multiple(() =>
		{
			Assert.That(Directory.GetFiles(_paths.KeysDirectory, "*.unreadable-*"), Is.Empty);
			Assert.That(_notifications.Snapshot(), Is.Empty);
			Assert.That(_log.Errors, Is.EqualTo(1));
		});

		_time.Advance(FileHostIdentityKeyProvider.RetryInterval);
		Assert.That(await provider.GetPublicKey(), Is.EqualTo(original));
	}

	[Test]
	public async Task Providers_creating_the_key_at_the_same_time_end_up_with_the_one_on_disk()
	{
		var providers = Enumerable.Range(0, 8).Select(_ => NewProvider()).ToList();

		await Task.WhenAll(providers.Select(provider => Task.Run(async () =>
		{
			try
			{
				await provider.GetPublicKey();
			}
			catch (HostIdentityUnavailableException)
			{
			}
		})));
		_time.Advance(FileHostIdentityKeyProvider.RetryInterval);

		var keys = new List<byte[]>();
		foreach (var provider in providers)
		{
			keys.Add(await provider.GetPublicKey());
		}

		var onDisk = await NewProvider().GetPublicKey();
		Assert.That(keys, Has.All.EqualTo(onDisk), "a key that appeared meanwhile is loaded, never replaced");
	}

	[Test]
	public async Task A_held_lock_makes_an_existing_key_unavailable_until_released_and_then_loads_it()
	{
		var original = await NewProvider().GetPublicKey();
		var provider = NewProvider();

		await using (HoldLock())
		{
			Assert.ThrowsAsync<HostIdentityUnavailableException>(async () => await provider.GetPublicKey());
		}

		_time.Advance(FileHostIdentityKeyProvider.RetryInterval);

		Assert.Multiple(async () =>
		{
			Assert.That(await provider.GetPublicKey(), Is.EqualTo(original));
			Assert.That(Directory.GetFiles(_paths.KeysDirectory, "*.tmp"), Is.Empty);
			Assert.That(_notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_held_lock_keeps_a_missing_key_from_being_created_until_released()
	{
		var provider = NewProvider();

		await using (HoldLock())
		{
			Assert.ThrowsAsync<HostIdentityUnavailableException>(async () => await provider.GetPublicKey());
			Assert.That(File.Exists(KeyPath), Is.False);
		}

		_time.Advance(FileHostIdentityKeyProvider.RetryInterval);
		var created = await provider.GetPublicKey();

		Assert.That(await NewProvider().GetPublicKey(), Is.EqualTo(created));
	}

	private FileStream HoldLock()
		=> new($"{KeyPath}.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

	[Test]
	public async Task A_signature_verifies_with_the_published_point()
	{
		var provider = NewProvider();
		var point = await provider.GetPublicKey();

		var signature = await provider.Sign("message"u8.ToArray());

		using var verifier = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		});
		Assert.That(verifier.VerifyData("message"u8,
				signature,
				HashAlgorithmName.SHA256,
				DSASignatureFormat.Rfc3279DerSequence),
			Is.True);
	}

	private FileHostIdentityKeyProvider NewProvider()
	{
		var services = new ServiceCollection()
			.AddSingleton<IAppPreferenceRepository>(_preferences)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();

		return new FileHostIdentityKeyProvider(_protection,
			_paths,
			services.GetRequiredService<IServiceScopeFactory>(),
			_notifications,
			TestLocalization.Resolver,
			_time,
			new LoggerConfiguration().WriteTo.Sink(_log).CreateLogger());
	}

	private static async Task Eventually(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met within 15 seconds.");
			}

			await Task.Delay(20);
		}
	}

	private sealed class InMemoryPreferences : IAppPreferenceRepository
	{
		public Dictionary<string, string> Values { get; } = [];

		public int FailingReads { get; set; }

		public int FailingWrites { get; set; }

		public bool Has(string key)
		{
			lock (Values)
			{
				return Values.ContainsKey(key);
			}
		}

		public Task<AppPreferenceEntity?> GetByKey(string key)
		{
			lock (Values)
			{
				if (FailingReads > 0)
				{
					FailingReads--;
					throw Locked();
				}

				return Task.FromResult(Values.TryGetValue(key, out var value)
					? new AppPreferenceEntity { Key = key, Value = value }
					: null);
			}
		}

		public Task SetValue(string key, string value)
		{
			lock (Values)
			{
				if (FailingWrites > 0)
				{
					FailingWrites--;
					throw Locked();
				}

				Values[key] = value;
			}

			return Task.CompletedTask;
		}
	}

	private static DbUpdateException Locked()
		=> new("An error occurred while saving the entity changes.",
			new InvalidOperationException("SQLite Error 5: 'database is locked'."));

	private sealed class ErrorCountingSink : ILogEventSink
	{
		private int _errors;

		public int Errors => Volatile.Read(ref _errors);

		public void Emit(LogEvent logEvent)
		{
			if (logEvent.Level == LogEventLevel.Error)
			{
				Interlocked.Increment(ref _errors);
			}
		}
	}
}

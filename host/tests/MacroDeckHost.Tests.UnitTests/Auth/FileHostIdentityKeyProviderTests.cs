using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[TestFixture]
public class FileHostIdentityKeyProviderTests
{
	private TestPaths _paths = null!;
	private EphemeralDataProtectionProvider _protection = null!;
	private InMemoryPreferences _preferences = null!;
	private UserNotificationStore _notifications = null!;

	private string KeyPath => Path.Combine(_paths.KeysDirectory, FileHostIdentityKeyProvider.KeyFileName);

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_protection = new EphemeralDataProtectionProvider();
		_preferences = new InMemoryPreferences();
		_notifications = new UserNotificationStore();
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
	public void The_same_key_comes_back_after_a_restart()
	{
		var first = NewProvider().PublicKey;
		var second = NewProvider().PublicKey;

		Assert.Multiple(() =>
		{
			Assert.That(second, Is.EqualTo(first));
			Assert.That(first, Has.Length.EqualTo(65));
			Assert.That(first[0], Is.EqualTo(0x04));
		});
	}

	[Test]
	public void The_first_key_on_an_existing_host_is_created_silently_and_recorded()
	{
		NewProvider().Sign("message"u8);

		Assert.Multiple(() =>
		{
			Assert.That(_notifications.Snapshot(), Is.Empty, "devices paired before the upgrade are unpinned");
			Assert.That(_preferences.Values, Does.ContainKey("identity.issued"));
		});
	}

	[Test]
	public void A_key_recreated_after_one_was_issued_tells_the_user_to_pair_again()
	{
		NewProvider().Sign("message"u8);
		File.Delete(KeyPath);

		NewProvider().Sign("message"u8);

		Assert.That(_notifications.Snapshot().Single().Kind, Is.EqualTo(UserNotificationKind.Security));
	}

	[Test]
	public void An_unprotectable_key_is_renewed_and_the_old_file_is_kept_aside()
	{
		File.WriteAllBytes(KeyPath, [1, 2, 3]);

		var provider = NewProvider();
		var publicKey = provider.PublicKey;

		var aside = Directory.GetFiles(_paths.KeysDirectory, "host-identity.key.unreadable-*");
		Assert.Multiple(() =>
		{
			Assert.That(aside, Has.Length.EqualTo(1));
			Assert.That(File.ReadAllBytes(aside[0]), Is.EqualTo(new byte[] { 1, 2, 3 }));
			Assert.That(NewProvider().PublicKey, Is.EqualTo(publicKey));
			Assert.That(_notifications.Snapshot().Single().Kind, Is.EqualTo(UserNotificationKind.Security));
		});
	}

	[Test]
	public void An_access_error_is_never_renewed_and_a_later_call_recovers()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Ignore("Unix file modes simulate the access error.");
			return;
		}

		var original = NewProvider().PublicKey;
		File.SetUnixFileMode(KeyPath, UnixFileMode.None);
		var provider = NewProvider();

		Assert.Throws<HostIdentityUnavailableException>(() => _ = provider.PublicKey);
		Assert.Multiple(() =>
		{
			Assert.That(Directory.GetFiles(_paths.KeysDirectory, "*.unreadable-*"), Is.Empty);
			Assert.That(_notifications.Snapshot(), Is.Empty);
		});

		File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		Assert.That(provider.PublicKey, Is.EqualTo(original));
	}

	[Test]
	public void A_signature_verifies_with_the_published_point()
	{
		var provider = NewProvider();
		var point = provider.PublicKey;

		var signature = provider.Sign("message"u8);

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
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());
	}

	private sealed class InMemoryPreferences : IAppPreferenceRepository
	{
		public Dictionary<string, string> Values { get; } = [];

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(Values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);

		public Task SetValue(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}
}

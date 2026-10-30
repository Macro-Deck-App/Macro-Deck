using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Secrets;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MacroDeckHost.Tests.UnitTests.Backups;

// A backup is what the user falls back on, so it must never look complete when it is not, and the key that
// opens it must never change behind the user's back.
[NonParallelizable]
public class BackupIntegrityTests
{
	private InProcessHost _host = null!;
	private readonly UnreadableSecrets _unreadable = new();

	[SetUp]
	public async Task SetUp()
	{
		_unreadable.Active = false;
		_host = await InProcessHost.StartAsync(services =>
		{
			services.RemoveAll<ISecretService>();
			services.AddScoped<ISecretService>(provider => new FailingSecretService(
				ActivatorUtilities.CreateInstance<SecretService>(provider),
				_unreadable));
		});
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
	}

	[Test]
	public async Task A_backup_that_could_not_read_a_file_says_it_is_incomplete()
	{
		var resources = Path.Combine(new MacroDeckPaths().DataRootDirectory, "resources");
		Directory.CreateDirectory(resources);
		var unreadable = Path.Combine(resources, "in-use.bin");
		await File.WriteAllBytesAsync(unreadable, new byte[1024]);

		JsonElement created;
		using (MakeUnreadable(unreadable))
		{
			created = await CreateBackup();
		}

		var notifications = _host.Services.GetRequiredService<IUserNotificationStore>().Snapshot();
		var incomplete = await IncompleteTitle();
		Assert.Multiple(() =>
		{
			Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
			Assert.That(notifications.Select(notification => notification.Title),
				Does.Contain(incomplete));
		});
	}

	[Test]
	public async Task A_backup_of_readable_files_raises_no_incomplete_warning()
	{
		var created = await CreateBackup();

		var notifications = _host.Services.GetRequiredService<IUserNotificationStore>().Snapshot();
		var incomplete = await IncompleteTitle();
		Assert.Multiple(() =>
		{
			Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
			Assert.That(notifications.Select(notification => notification.Title),
				Does.Not.Contain(incomplete));
		});
	}

	[Test]
	public async Task A_recovery_key_replaced_because_it_became_unreadable_reads_as_never_shown()
	{
		Assert.That((await CreateBackup()).GetProperty("success").GetBoolean(), Is.True);
		await _host.SendAsync(HttpMethod.Post, "/api/backups/recovery-key/acknowledge", loopback: true);
		var exported = await RecoveryKeyState();

		_unreadable.Active = true;
		var whileUnreadable = await CreateBackup();
		_unreadable.Active = false;
		var afterwards = await RecoveryKeyState();

		Assert.Multiple(() =>
		{
			Assert.That(exported.GetProperty("exportedAt").ValueKind, Is.EqualTo(JsonValueKind.String));
			Assert.That(whileUnreadable.GetProperty("success").GetBoolean(), Is.True, whileUnreadable.ToString());
			Assert.That(afterwards.GetProperty("keyId").GetString(),
				Is.Not.EqualTo(exported.GetProperty("keyId").GetString()));
			Assert.That(afterwards.TryGetProperty("exportedAt", out var exportedAt)
					&& exportedAt.ValueKind == JsonValueKind.String,
				Is.False,
				"the replacement key still reads as the one the user saved");
		});
	}

	private async Task<JsonElement> CreateBackup()
	{
		return await InProcessHost.ReadJsonAsync(await _host.SendAsync(HttpMethod.Post,
			"/api/backups",
			new { note = "integrity" },
			loopback: true));
	}

	private async Task<string> IncompleteTitle()
	{
		using var scope = _host.Services.CreateScope();
		var culture = (await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetLocalization()).Culture;
		return _host.Services.GetRequiredService<ILocalizationResolver>()
			.Resolve(AppStrings.Notifications.Backup.Incomplete(), culture);
	}

	private async Task<JsonElement> RecoveryKeyState()
	{
		return await InProcessHost.ReadJsonAsync(
			await _host.SendAsync(HttpMethod.Get, "/api/backups/recovery-key", loopback: true));
	}

	private static IDisposable MakeUnreadable(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		}

		return new NoPermissions(path);
	}

	[UnsupportedOSPlatform("windows")]
	private sealed class NoPermissions : IDisposable
	{
		private readonly string _path;
		private readonly UnixFileMode _previous;

		public NoPermissions(string path)
		{
			_path = path;
			_previous = File.GetUnixFileMode(path);
			File.SetUnixFileMode(path, UnixFileMode.None);
		}

		public void Dispose()
		{
			File.SetUnixFileMode(_path, _previous);
		}
	}

	private sealed class UnreadableSecrets
	{
		public bool Active { get; set; }
	}

	// What a lost DataProtection key looks like to the secret store: the row is there, the value is not.
	private sealed class FailingSecretService(ISecretService inner, UnreadableSecrets unreadable) : ISecretService
	{
		public Task<Guid> Create(string value, SecretKind kind)
		{
			return inner.Create(value, kind);
		}

		public Task<bool> Replace(Guid id, string value)
		{
			return inner.Replace(id, value);
		}

		public Task<bool> Delete(Guid id)
		{
			return inner.Delete(id);
		}

		public Task<Guid?> Clone(Guid id)
		{
			return inner.Clone(id);
		}

		public Task<string?> Reveal(Guid id)
		{
			return inner.Reveal(id);
		}

		public Task<string?> Resolve(Guid id)
		{
			return unreadable.Active ? throw new CryptographicException("The key ring changed.") : inner.Resolve(id);
		}

		public Task<SecretMaterial?> ExportForArchive(Guid id)
		{
			return inner.ExportForArchive(id);
		}
	}
}

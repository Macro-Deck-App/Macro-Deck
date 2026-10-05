using System.Collections.Concurrent;
using MacroDeckHost.Application.Feedback;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Feedback;

[TestFixture]
internal sealed class GitHubStarPromptServiceTests
{
	private static readonly DateTime InstalledAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	private MemoryPreferences _preferences = null!;
	private FakeTimeProvider _time = null!;
	private ServiceProvider _services = null!;
	private GitHubStarPromptService _prompt = null!;

	[SetUp]
	public void SetUp()
	{
		_preferences = new MemoryPreferences();
		_time = new FakeTimeProvider();
		_services = new ServiceCollection()
			.AddScoped<IAppPreferenceRepository>(_ => _preferences)
			.BuildServiceProvider();
		_prompt = new GitHubStarPromptService(_services.GetRequiredService<IServiceScopeFactory>(), _time);
	}

	[TearDown]
	public void TearDown()
	{
		_prompt.Dispose();
		_services.Dispose();
	}

	[Test]
	public async Task It_is_not_due_before_the_installation_is_seven_days_old()
	{
		_preferences.Seed(AppPreferenceService.InstallationIdKey, Guid.NewGuid().ToString("D"), InstalledAt);
		_time.Now = new DateTimeOffset(InstalledAt) + TimeSpan.FromDays(7) - TimeSpan.FromHours(1);

		Assert.That((await _prompt.GetPrompt()).Due, Is.False);
	}

	[Test]
	public async Task It_is_due_once_the_installation_is_seven_days_old()
	{
		_preferences.Seed(AppPreferenceService.InstallationIdKey, Guid.NewGuid().ToString("D"), InstalledAt);
		_time.Now = new DateTimeOffset(InstalledAt) + TimeSpan.FromDays(7);

		Assert.That((await _prompt.GetPrompt()).Due, Is.True);
	}

	[Test]
	public async Task It_is_never_due_again_after_it_was_shown()
	{
		_preferences.Seed(AppPreferenceService.InstallationIdKey, Guid.NewGuid().ToString("D"), InstalledAt);
		_time.Now = new DateTimeOffset(InstalledAt) + TimeSpan.FromDays(8);

		await _prompt.MarkShown(CancellationToken.None);
		_time.Now += TimeSpan.FromDays(365);

		Assert.That((await _prompt.GetPrompt()).Due, Is.False);
	}

	[Test]
	public async Task It_is_not_due_without_an_installation_anchor()
	{
		_time.Now = new DateTimeOffset(InstalledAt) + TimeSpan.FromDays(30);

		Assert.That((await _prompt.GetPrompt()).Due, Is.False);
	}

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		private readonly ConcurrentDictionary<string, AppPreferenceEntity> _rows = new();

		public void Seed(string key, string value, DateTime createdAt)
			=> _rows[key] = new AppPreferenceEntity { Key = key, Value = value, CreatedAt = createdAt, UpdatedAt = createdAt };

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_rows.TryGetValue(key, out var row) ? row : null);

		public Task SetValue(string key, string value)
		{
			var now = DateTime.Now;
			_rows.AddOrUpdate(key,
				_ => new AppPreferenceEntity { Key = key, Value = value, CreatedAt = now, UpdatedAt = now },
				(_, row) =>
				{
					row.Value = value;
					row.UpdatedAt = now;
					return row;
				});
			return Task.CompletedTask;
		}
	}
}

[TestFixture]
[NonParallelizable]
internal sealed class GitHubStarPromptInstallAnchorTests
{
	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;
	private FakeTimeProvider _time = null!;
	private ServiceProvider _services = null!;
	private GitHubStarPromptService _prompt = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);
		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		_time = new FakeTimeProvider();
		_services = new ServiceCollection()
			.AddScoped(_ => new DatabaseContext(Log.Logger, _paths))
			.AddScoped<IAppPreferenceRepository, AppPreferenceRepository>()
			.BuildServiceProvider();
		_prompt = new GitHubStarPromptService(_services.GetRequiredService<IServiceScopeFactory>(), _time);
	}

	[TearDown]
	public void TearDown()
	{
		_prompt.Dispose();
		_services.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task An_anchor_seeded_at_startup_counts_seven_days_from_its_creation()
	{
		var before = DateTimeOffset.UtcNow;
		EarlyAppPreferenceReader.EnsureValue(_paths, AppPreferenceService.InstallationIdKey, Guid.NewGuid().ToString("D"));
		var after = DateTimeOffset.UtcNow;

		await AssertBoundary(before, after);
	}

	[Test]
	public async Task An_anchor_written_by_the_repository_counts_seven_days_from_its_creation()
	{
		var before = DateTimeOffset.UtcNow;
		using (var scope = _services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
				.SetValue(AppPreferenceService.InstallationIdKey, Guid.NewGuid().ToString("D"));
		}

		var after = DateTimeOffset.UtcNow;

		await AssertBoundary(before, after);
	}

	[Test]
	public async Task An_anchor_stored_without_an_offset_is_read_as_local_time()
	{
		var local = new DateTime(2026, 1, 1, 12, 0, 0);
		await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString()))
		{
			await connection.OpenAsync();
			await using var insert = connection.CreateCommand();
			insert.CommandText = "INSERT INTO app_preference (ap_key, ap_value, ap_created_at, ap_updated_at) " +
				"VALUES ($key, $value, '2026-01-01 12:00:00', '2026-01-01 12:00:00')";
			insert.Parameters.AddWithValue("$key", AppPreferenceService.InstallationIdKey);
			insert.Parameters.AddWithValue("$value", Guid.NewGuid().ToString("D"));
			await insert.ExecuteNonQueryAsync();
		}

		var installedUtc = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime());

		_time.Now = installedUtc + TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1);
		var early = (await _prompt.GetPrompt()).Due;
		_time.Now = installedUtc + TimeSpan.FromDays(7);
		var due = (await _prompt.GetPrompt()).Due;

		Assert.Multiple(() =>
		{
			Assert.That(early, Is.False);
			Assert.That(due, Is.True);
		});
	}

	private async Task AssertBoundary(DateTimeOffset before, DateTimeOffset after)
	{
		_time.Now = before + TimeSpan.FromDays(7) - TimeSpan.FromHours(1);
		var early = (await _prompt.GetPrompt()).Due;
		_time.Now = after + TimeSpan.FromDays(7);
		var due = (await _prompt.GetPrompt()).Due;

		Assert.Multiple(() =>
		{
			Assert.That(early, Is.False);
			Assert.That(due, Is.True);
		});
	}
}

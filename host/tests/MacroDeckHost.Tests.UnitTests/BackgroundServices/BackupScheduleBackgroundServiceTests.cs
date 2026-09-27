using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class BackupScheduleBackgroundServiceTests
{
	private FakeTimeProvider _time = null!;
	private SchedulePreferences _preferences = null!;
	private RecordingBackupService _backups = null!;
	private BackupScheduleBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_preferences = new SchedulePreferences { LastRun = _time.Now.AddDays(-2) };
		_backups = new RecordingBackupService();

		var services = new ServiceCollection();
		services.AddScoped<IAppPreferenceService>(_ => _preferences);

		_service = new BackupScheduleBackgroundService(new StartedHostLifetime(),
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			_backups,
			_time,
			Log.Logger);
	}

	[TearDown]
	public void TearDown() => _service.Dispose();

	[Test]
	public async Task A_failing_schedule_check_is_retried_and_the_due_backup_still_runs()
	{
		_preferences.FailingReads = 1;

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => _time.ActiveTimerCount == 1);

		Assert.Multiple(() =>
		{
			Assert.That(_service.ExecuteTask!.IsFaulted, Is.False);
			Assert.That(_backups.Requests, Is.Empty);
		});

		_time.Advance(TimeSpan.FromHours(1));
		await WaitUntil(() => _backups.Requests.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(_backups.Requests.Single().Trigger, Is.EqualTo(BackupTrigger.Scheduled));
			Assert.That(_preferences.LastRun, Is.EqualTo(_time.Now));
		});
	}

	[Test]
	public async Task Stopping_the_host_while_the_scheduler_waits_ends_it_cleanly()
	{
		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => _time.ActiveTimerCount == 1);

		await _service.StopAsync(CancellationToken.None);

		Assert.That(_service.ExecuteTask!.IsCompletedSuccessfully, Is.True);
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("condition was not reached in time");
			}

			await Task.Delay(10);
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class RecordingBackupService : IBackupService
	{
		public List<CreateBackupRequest> Requests { get; } = [];

		public Task<Result<BackupDescriptor, BackupError>> Create(CreateBackupRequest request,
			CancellationToken cancellationToken = default)
		{
			lock (Requests)
			{
				Requests.Add(request);
			}

			return Task.FromResult(Result.Fail<BackupDescriptor, BackupError>(BackupError.Busy, "not in this test"));
		}

		public Task<Result<IReadOnlyList<BackupDescriptor>, BackupError>> List(
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupInspection, BackupError>> Inspect(BackupSourceRef source,
			string? recoveryKey,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupDescriptor, BackupError>> Import(BackupSourceRef source,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupExportHandle, BackupError>> OpenExport(Guid backupId,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<BackupError>> Delete(Guid backupId, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	private sealed class SchedulePreferences : IAppPreferenceService
	{
		public int FailingReads { get; set; }

		public DateTimeOffset? LastRun { get; set; }


		public Task<NativeUsbSettings> GetNativeUsb() => throw new NotSupportedException();

		public Task<NativeUsbSettings> SetNativeUsb(bool? enabled, IReadOnlyList<RememberedUsbDevice>? rememberedDevices)
			=> throw new NotSupportedException();

		public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

		public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();

		public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

		public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor, string? fontFamily)
			=> throw new NotSupportedException();

		public Task<Guid> GetInstallationId() => throw new NotSupportedException();

		public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

		public Task<LoggingSettings> SetLogging(LogEntryLevel? minimumLevel) => throw new NotSupportedException();

		public Task<NetworkSettings> GetNetwork() => throw new NotSupportedException();

		public Task<NetworkSettings> SetNetwork(int? publicPort,
			bool? tlsEnabled = null,
			string? tlsMode = null,
			int? tlsHttpsPort = null,
			bool? discoveryEnabled = null)
			=> throw new NotSupportedException();

		public Task<AdbSettings> GetAdb() => throw new NotSupportedException();

		public Task<AdbSettings> SetAdb(bool? enabled,
			string? executablePath,
			bool? usbConnectionsEnabled,
			string? defaultDeviceSerial,
			bool? stopServerOnExit,
			bool allowPlugins)
			=> throw new NotSupportedException();

		public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

		public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

		public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

		public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

		public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

		public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
			bool? checkForUpdates,
			bool? notifyOnUpdates,
			int? refreshIntervalMinutes,
			bool? autoUpdate = null)
			=> throw new NotSupportedException();

		public Task<BackupSettings> GetBackups()
		{
			if (FailingReads > 0)
			{
				FailingReads--;
				throw new ObjectDisposedException("SQLitePCL.sqlite3");
			}

			return Task.FromResult(new BackupSettings("daily", "03:00", DayOfWeek.Monday, 1, "keepLatest", 5, true, true));
		}

		public Task<BackupSettings> SetBackups(string? scheduleFrequency,
			string? scheduleTimeOfDay,
			string? scheduleDayOfWeek,
			int? scheduleDayOfMonth,
			string? retentionPolicy,
			int? retentionKeepLatest,
			bool? beforeHostUpdate,
			bool? beforePluginUpdate) => throw new NotSupportedException();

		public Task<DateTimeOffset?> GetBackupScheduleLastRun() => Task.FromResult(LastRun);

		public Task SetBackupScheduleLastRun(DateTimeOffset value)
		{
			LastRun = value;
			return Task.CompletedTask;
		}

		public Task<LocalizationSettings> GetLocalization() => throw new NotSupportedException();

		public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

		public Task<string> GetTimeFormat() => throw new NotSupportedException();

		public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();
	}
}

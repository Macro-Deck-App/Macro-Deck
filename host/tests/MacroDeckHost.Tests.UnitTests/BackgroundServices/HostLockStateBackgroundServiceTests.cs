using System.Runtime.CompilerServices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.HostLocking;
using MacroDeckHost.Integrations.System.Lock;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class HostLockStateBackgroundServiceTests
{
	[TearDown]
	public void TearDown() => LockStateSnapshot.Current.Set(null);

	[Test]
	public async Task A_transition_publishes_exactly_once()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var service = CreateService(reader, state, preferences, mediator);

		await service.Tick(CancellationToken.None);
		reader.Locked = true;
		await service.Tick(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(mediator.Published.OfType<HostLockStateChangedNotification>().Count(), Is.EqualTo(2));
			Assert.That(state.IsLocked, Is.True);
		});
	}

	[Test]
	public async Task An_unchanged_read_publishes_nothing()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var service = CreateService(reader, state, preferences, mediator);

		await service.Tick(CancellationToken.None);
		var afterFirst = mediator.Published.Count;
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.That(mediator.Published.Count, Is.EqualTo(afterFirst));
	}

	[Test]
	public async Task An_unsupported_reader_never_starts_the_loop()
	{
		var reader = new FakeLockStateReader { IsSupported = false, Locked = null };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var service = CreateService(reader, state, preferences, mediator);

		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(reader.ReadCount, Is.EqualTo(0));
			Assert.That(mediator.Published, Is.Empty);
			Assert.That(state.IsSupported, Is.False);
		});
	}

	[Test]
	public async Task A_signalled_unlock_is_published_without_waiting_for_a_poll()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = true };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = true, Signals = [false] };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.Tick(CancellationToken.None);
		var publishedAfterBaseline = mediator.Published.Count;

		await service.ConsumeWatcher(CancellationToken.None);

		var notifications = mediator.Published.OfType<HostLockStateChangedNotification>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(mediator.Published.Count, Is.EqualTo(publishedAfterBaseline + 1));
			Assert.That(notifications[^1].Locked, Is.False);
			Assert.That(state.IsLocked, Is.False);
		});
	}

	[Test]
	public async Task A_signalled_lock_is_published_without_waiting_for_a_poll()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = true, Signals = [true] };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.Tick(CancellationToken.None);
		var publishedAfterBaseline = mediator.Published.Count;

		await service.ConsumeWatcher(CancellationToken.None);

		var notifications = mediator.Published.OfType<HostLockStateChangedNotification>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(mediator.Published.Count, Is.EqualTo(publishedAfterBaseline + 1));
			Assert.That(notifications[^1].Locked, Is.True);
			Assert.That(state.IsLocked, Is.True);
		});
	}

	[Test]
	public async Task A_poll_that_agrees_with_a_signalled_change_publishes_nothing()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = true, Signals = [false] };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.Tick(CancellationToken.None);
		await service.ConsumeWatcher(CancellationToken.None);
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.That(mediator.Published.OfType<HostLockStateChangedNotification>().Count(), Is.EqualTo(1));
	}

	[Test]
	public async Task An_unsupported_watcher_leaves_the_poll_as_the_only_mechanism()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = false, ThrowIfWatched = true };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.ConsumeWatcher(CancellationToken.None);

		await service.Tick(CancellationToken.None);
		reader.Locked = true;
		await service.Tick(CancellationToken.None);

		Assert.That(mediator.Published.OfType<HostLockStateChangedNotification>().Count(), Is.EqualTo(2));
	}

	[Test]
	public async Task A_failing_watcher_leaves_the_poll_running()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = true, Signals = [true], ThrowAfterSignals = true };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.ConsumeWatcher(CancellationToken.None);

		var notifications = mediator.Published.OfType<HostLockStateChangedNotification>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Locked, Is.True);
			Assert.That(state.IsLocked, Is.True);
		});

		reader.Locked = false;
		await service.Tick(CancellationToken.None);

		notifications = mediator.Published.OfType<HostLockStateChangedNotification>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(2));
			Assert.That(notifications[^1].Locked, Is.False);
		});
	}

	[Test]
	public async Task A_signalled_change_establishes_the_state_before_any_poll()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = null };
		var state = new HostLockState();
		var preferences = new FakeLockScreenPreferenceService();
		var mediator = new RecordingMediator();
		var watcher = new FakeLockStateWatcher { IsSupported = true, Signals = [true] };
		var service = CreateService(reader, state, preferences, mediator, watcher);

		await service.ConsumeWatcher(CancellationToken.None);

		var notifications = mediator.Published.OfType<HostLockStateChangedNotification>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Locked, Is.True);
			Assert.That(state.IsLocked, Is.True);
		});
	}

	private static HostLockStateBackgroundService CreateService(
		FakeLockStateReader reader,
		HostLockState state,
		IAppPreferenceService preferences,
		RecordingMediator mediator,
		FakeLockStateWatcher? watcher = null)
		=> new(new StartedHostLifetime(),
			reader,
			watcher ?? new FakeLockStateWatcher { IsSupported = false, ThrowIfWatched = true },
			state,
			preferences.GetLockScreen,
			mediator,
			Log.Logger);

	private sealed class FakeLockStateReader : ILockStateReader
	{
		public bool IsSupported { get; init; } = true;

		public string? UnsupportedReason => IsSupported ? null : "not supported here";

		public bool? Locked { get; set; }

		public int ReadCount { get; private set; }

		public bool? IsLocked()
		{
			ReadCount++;
			return Locked;
		}
	}

	private sealed class FakeLockStateWatcher : ILockStateWatcher
	{
		public bool IsSupported { get; init; }

		public IReadOnlyList<bool> Signals { get; init; } = [];

		public bool ThrowIfWatched { get; init; }

		public bool ThrowAfterSignals { get; init; }

		public async IAsyncEnumerable<bool> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
		{
			if (ThrowIfWatched)
			{
				throw new InvalidOperationException("WatchAsync must not be called for an unsupported watcher.");
			}

			foreach (var signal in Signals)
			{
				await Task.Yield();
				yield return signal;
			}

			if (ThrowAfterSignals)
			{
				throw new InvalidOperationException("Simulated watcher failure.");
			}
		}

		public void Dispose()
		{
		}
	}

	private sealed class FakeLockScreenPreferenceService : IAppPreferenceService
	{
		public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

		public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

		public Task<LockScreenSettings> GetLockScreen() => Task.FromResult(new LockScreenSettings(false));

		public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

		public Task<BackupSettings> GetBackups() => throw new NotSupportedException();

		public Task<BackupSettings> SetBackups(string? scheduleFrequency,
			string? scheduleTimeOfDay,
			string? scheduleDayOfWeek,
			int? scheduleDayOfMonth,
			string? retentionPolicy,
			int? retentionKeepLatest,
			bool? beforeHostUpdate,
			bool? beforePluginUpdate) => throw new NotSupportedException();

		public Task<DateTimeOffset?> GetBackupScheduleLastRun() => throw new NotSupportedException();

		public Task SetBackupScheduleLastRun(DateTimeOffset value) => throw new NotSupportedException();

		public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

		public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor)
			=> throw new NotSupportedException();

		public Task<Guid> GetInstallationId() => throw new NotSupportedException();

		public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

		public Task<LoggingSettings> SetLogging(Application.Logging.LogEntryLevel? minimumLevel)
			=> throw new NotSupportedException();

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
			string? defaultDeviceSerial)
			=> throw new NotSupportedException();

		public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

		public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();

		public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

		public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
			bool? checkForUpdates,
			bool? notifyOnUpdates,
			int? refreshIntervalMinutes)
			=> throw new NotSupportedException();

		public Task<LocalizationSettings> GetLocalization() => throw new NotSupportedException();

		public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

		public Task<string> GetTimeFormat() => Task.FromResult(AppPreferenceService.TimeFormatSystem);

		public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = new(true);
		public CancellationToken ApplicationStopped { get; } = new(true);

		public void StopApplication()
		{
		}
	}
}

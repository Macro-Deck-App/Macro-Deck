using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class FakeAdbPreferenceService : IAppPreferenceService
{
	public AdbSettings AdbSettings { get; set; } = new(true, "fake-adb", true, null);

	public Task<AdbSettings> GetAdb() => Task.FromResult(AdbSettings);

	public Task<AdbSettings> SetAdb(bool? enabled,
		string? executablePath,
		bool? usbConnectionsEnabled,
		string? defaultDeviceSerial)
		=> throw new NotSupportedException();

	public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

	public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor) =>
		throw new NotSupportedException();

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

	public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

	public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();

	public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

	public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

	public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

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
	public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

	public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
		bool? checkForUpdates,
		bool? notifyOnUpdates,
		int? refreshIntervalMinutes)
		=> throw new NotSupportedException();

	// Answered rather than refused: anything resolving a localized string reaches this, and refusing
	// would make an unrelated fake the reason a test fails.
	public Task<LocalizationSettings> GetLocalization() => Task.FromResult(new LocalizationSettings("en", false));

	public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();
}

internal sealed class ManualTimeProvider : TimeProvider
{
	public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => Now;

	public void Advance(TimeSpan delta) => Now += delta;
}

internal sealed class AdbManagerHarness : IDisposable
{
	private readonly ServiceProvider _serviceProvider;

	public FakeAdbProcessRunner Runner { get; } = new();

	public FakeAdbPreferenceService PreferenceService { get; } = new();

	public TestPaths Paths { get; }

	public FakeHostListenerState ListenerState { get; }

	public ManualTimeProvider TimeProvider { get; } = new();

	public AdbManager Manager { get; }

	public AdbManagerHarness(int publicPort = 8193, TestPaths? paths = null)
	{
		Paths = paths ?? new TestPaths();
		ListenerState = new FakeHostListenerState { PublicPort = publicPort };

		var services = new ServiceCollection();
		services.AddScoped<IAppPreferenceService>(_ => PreferenceService);
		_serviceProvider = services.BuildServiceProvider();

		Manager = new AdbManager(_serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			Runner,
			Paths,
			ListenerState,
			TimeProvider,
			new LoggerConfiguration().CreateLogger());
	}

	public void Dispose()
	{
		Manager.Dispose();
		_serviceProvider.Dispose();
		Paths.Cleanup();
	}
}

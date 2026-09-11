using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>An <see cref="IAppPreferenceService" /> that answers onboarding and nothing else: every
/// other preference throws, so a test that reaches one by accident fails loudly instead of reading a
/// default that was never meant to stand in for anything.</summary>
internal sealed class FakeOnboardingPreferences : IAppPreferenceService
{
	public bool OnboardingPending { get; set; }

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
		string? defaultDeviceSerial)
		=> throw new NotSupportedException();

	public Task<OnboardingSettings> GetOnboarding()
		=> Task.FromResult(new OnboardingSettings(OnboardingPending));

	public Task<OnboardingSettings> SetOnboarding(bool? pending)
	{
		OnboardingPending = pending ?? false;
		return Task.FromResult(new OnboardingSettings(OnboardingPending));
	}

	public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

	public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

	public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

	public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
		bool? checkForUpdates,
		bool? notifyOnUpdates,
		int? refreshIntervalMinutes)
		=> throw new NotSupportedException();

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

	public Task<LocalizationSettings> GetLocalization() => throw new NotSupportedException();

	public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

	public Task<string> GetTimeFormat() => Task.FromResult(AppPreferenceService.TimeFormatSystem);

	public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();
}

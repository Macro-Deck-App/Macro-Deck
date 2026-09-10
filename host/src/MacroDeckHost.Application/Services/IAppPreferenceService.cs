using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Application.Services;

public record AppearanceSettings(string ThemeMode, string AccentColor);

public record LoggingSettings(LogEntryLevel MinimumLevel, LogEntryLevel DefaultMinimumLevel);

public record NetworkSettings(
	int PublicPort,
	int DefaultPublicPort,
	int ActivePublicPort,
	bool OverriddenByEnvironment,
	bool ConfiguredPortIgnored,
	bool PublicListenerUnavailable,
	bool TlsEnabled,
	PublicTlsMode TlsMode,
	int TlsHttpsPort,
	int DefaultTlsHttpsPort,
	bool ActiveTlsEnabled,
	PublicTlsMode ActiveTlsMode,
	int? ActiveTlsHttpsPort,
	PublicTlsFailure TlsFailure,
	PublicTlsRejection TlsRejection,
	bool TlsCertificateConfigured,
	PublicTlsCertificateSource? TlsCertificateSource,
	string? TlsCertificateSubject,
	string? TlsCertificateFingerprint,
	DateTimeOffset? TlsCertificateNotBefore,
	DateTimeOffset? TlsCertificateNotAfter,
	bool TlsCertificateExpired,
	bool TlsCertificateNotYetValid,
	string? ActiveTlsCertificateFingerprint,
	bool TlsCertificateIssuedByAuthority,
	bool TlsAuthorityConfigured,
	string? TlsAuthoritySubject,
	string? TlsAuthorityFingerprint,
	DateTimeOffset? TlsAuthorityNotBefore,
	DateTimeOffset? TlsAuthorityNotAfter,
	bool DiscoveryEnabled);

public record AdbSettings(
	bool Enabled,
	string? ExecutablePath,
	bool UsbConnectionsEnabled,
	string? DefaultDeviceSerial);

public record DeveloperSettings(bool Enabled);

public record LockScreenSettings(bool Enabled);

public record OnboardingSettings(bool Pending);

public record BackupSettings(
	string ScheduleFrequency,
	string ScheduleTimeOfDay,
	DayOfWeek ScheduleDayOfWeek,
	int ScheduleDayOfMonth,
	string RetentionPolicy,
	int RetentionKeepLatest,
	bool BeforeHostUpdate,
	bool BeforePluginUpdate);

public record ExtensionSettings(
	bool StoreEnabled,
	bool CheckForUpdates,
	bool NotifyOnUpdates,
	int RefreshIntervalMinutes);

/// <param name="Culture">The culture in effect - either the stored choice, or the operating system's.</param>
/// <param name="FollowSystem">
/// True while no language has been chosen, so <paramref name="Culture" /> tracks the operating system
/// and will keep tracking it if that changes. A client shows this as its own "System" entry rather than
/// as the culture it currently resolves to.
/// </param>
public record LocalizationSettings(string Culture, bool FollowSystem = false);

public interface IAppPreferenceService
{
	Task<AppearanceSettings> GetAppearance();

	Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor);

	Task<Guid> GetInstallationId();

	Task<LoggingSettings> GetLogging();

	Task<LoggingSettings> SetLogging(LogEntryLevel? minimumLevel);

	Task<NetworkSettings> GetNetwork();

	// Unlike the TLS parameters, a null discoveryEnabled keeps the stored value, so a caller that only
	// changes the port or TLS never switches network discovery on or off as a side effect.
	Task<NetworkSettings> SetNetwork(int? publicPort,
		bool? tlsEnabled = null,
		string? tlsMode = null,
		int? tlsHttpsPort = null,
		bool? discoveryEnabled = null);

	Task<AdbSettings> GetAdb();

	Task<AdbSettings> SetAdb(bool? enabled,
		string? executablePath,
		bool? usbConnectionsEnabled,
		string? defaultDeviceSerial);

	Task<DeveloperSettings> GetDeveloper();

	Task<DeveloperSettings> SetDeveloper(bool? enabled);

	Task<OnboardingSettings> GetOnboarding();

	Task<OnboardingSettings> SetOnboarding(bool? pending);

	Task<LockScreenSettings> GetLockScreen();

	Task<LockScreenSettings> SetLockScreen(bool? enabled);

	Task<BackupSettings> GetBackups();

	Task<BackupSettings> SetBackups(string? scheduleFrequency,
		string? scheduleTimeOfDay,
		string? scheduleDayOfWeek,
		int? scheduleDayOfMonth,
		string? retentionPolicy,
		int? retentionKeepLatest,
		bool? beforeHostUpdate,
		bool? beforePluginUpdate);

	Task<DateTimeOffset?> GetBackupScheduleLastRun();

	Task SetBackupScheduleLastRun(DateTimeOffset value);
	Task<ExtensionSettings> GetExtensions();

	Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
		bool? checkForUpdates,
		bool? notifyOnUpdates,
		int? refreshIntervalMinutes);

	Task<LocalizationSettings> GetLocalization();

	/// <summary>
	/// Stores the chosen language, or - for a null or blank <paramref name="culture" /> - clears the
	/// choice so the operating system's language applies again.
	/// </summary>
	Task<LocalizationSettings> SetLocalization(string? culture);
}

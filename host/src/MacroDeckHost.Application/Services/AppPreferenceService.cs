using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Persistence.Repositories;

namespace MacroDeckHost.Application.Services;

public partial class AppPreferenceService : IAppPreferenceService
{
	public const string ThemeModeKey = "appearance.themeMode";

	public const string AccentColorKey = "appearance.accentColor";

	public const string FontFamilyKey = "appearance.fontFamily";

	// Keyed under the historical "telemetry." prefix: the value seeds the key ring's KEK store
	// identity, so renaming it would orphan every existing installation's encrypted key material.
	public const string InstallationIdKey = "telemetry.installationId";
	public const string MinimumLogLevelKey = "logging.minimumLevel";
	public const string PublicPortKey = "network.publicPort";

	// Read raw by EarlyAppPreferenceReader before the DI container exists, because Kestrel's
	// endpoints - and therefore the TLS decision - are configured before the host is built.
	public const string TlsEnabledKey = "network.tls.enabled";
	public const string TlsModeKey = "network.tls.mode";
	public const string TlsHttpsPortKey = "network.tls.httpsPort";

	public const string DiscoveryEnabledKey = "network.discovery.enabled";

	public const string AdbEnabledKey = "adb.enabled";
	public const string AdbExecutablePathKey = "adb.executablePath";
	public const string AdbUsbConnectionsEnabledKey = "adb.usbConnectionsEnabled";
	public const string AdbDefaultDeviceSerialKey = "adb.defaultDeviceSerial";

	public const string DeveloperModeKey = "developer.mode";

	public const string LockScreenEnabledKey = "lock.clientLockScreen";

	// Armed by AuthService.Setup rather than defaulting to true, so that an installation which
	// already has an account never starts owing an onboarding wizard after an update.
	public const string OnboardingPendingKey = "onboarding.pending";

	// Written and read only by the backup recovery key service. Deliberately outside BackupSettings so
	// a settings update can never point the installation at a secret row that does not exist, which
	// would orphan every backup already taken.
	public const string BackupRecoveryKeySecretIdKey = "backups.recoveryKeySecretId";
	public const string BackupRecoveryKeyCreatedAtKey = "backups.recoveryKeyCreatedAt";
	public const string BackupRecoveryKeyExportedAtKey = "backups.recoveryKeyExportedAt";

	// Written and read only by the Connect credential store. Deliberately kept out of every backup
	// archive (see BackupComponentGroups.PreferenceKeyDenyPrefixes and BackupSnapshotSource) so a cloud
	// credential never travels inside one.
	public const string ConnectCredentialSecretIdKey = "connect.credentialSecretId";
	public const string ConnectCredentialSubjectKey = "connect.credentialSubject";
	public const string ConnectCredentialCachedDisplayNameKey = "connect.credentialCachedDisplayName";
	public const string ConnectCredentialCachedPictureUrlKey = "connect.credentialCachedPictureUrl";
	public const string ConnectCredentialIssuedAtKey = "connect.credentialIssuedAt";
	public const string ConnectSuspensionRetryNotBeforeKey = "connect.suspensionRetryNotBefore";

	public const string BackupScheduleFrequencyKey = "backups.schedule.frequency";
	public const string BackupScheduleTimeOfDayKey = "backups.schedule.timeOfDay";
	public const string BackupScheduleDayOfWeekKey = "backups.schedule.dayOfWeek";
	public const string BackupScheduleDayOfMonthKey = "backups.schedule.dayOfMonth";
	public const string BackupScheduleLastRunAtKey = "backups.schedule.lastRunAt";
	public const string BackupRetentionPolicyKey = "backups.retention.policy";
	public const string BackupRetentionKeepLatestKey = "backups.retention.keepLatest";
	public const string BackupBeforeHostUpdateKey = "backups.triggers.beforeHostUpdate";
	public const string BackupBeforePluginUpdateKey = "backups.triggers.beforePluginUpdate";
	public const string LocalizationCultureKey = "localization.culture";
	public const string LocalizationTimeFormatKey = "localization.timeFormat";

	public const string TimeFormatSystem = "system";
	public const string TimeFormat12h = "12h";
	public const string TimeFormat24h = "24h";

	public const string ExtensionsStoreEnabledKey = "extensions.storeEnabled";
	public const string ExtensionsCheckForUpdatesKey = "extensions.checkForUpdates";
	public const string ExtensionsNotifyOnUpdatesKey = "extensions.notifyOnUpdates";
	public const string ExtensionsRefreshIntervalMinutesKey = "extensions.refreshIntervalMinutes";

	public const bool DefaultExtensionsStoreEnabled = true;
	public const bool DefaultExtensionsCheckForUpdates = true;
	public const bool DefaultExtensionsNotifyOnUpdates = true;
	public const int DefaultExtensionsRefreshIntervalMinutes = 60;

	// The issue requires the store to check for updates at least hourly, so a longer interval must never
	// be settable - only the lower bound protects the registry from being polled too aggressively.
	public const int MinExtensionsRefreshIntervalMinutes = 15;
	public const int MaxExtensionsRefreshIntervalMinutes = 60;

	public const string DefaultThemeMode = "system";
	public const string DefaultAccentColor = "#2196F3";

	public const bool DefaultAdbUsbConnectionsEnabled = true;

	public const string BackupScheduleOff = "off";
	public const string BackupScheduleDaily = "daily";
	public const string BackupScheduleWeekly = "weekly";
	public const string BackupScheduleMonthly = "monthly";

	public const string DefaultBackupScheduleFrequency = BackupScheduleOff;
	public const string DefaultBackupScheduleTimeOfDay = "03:00";
	public const DayOfWeek DefaultBackupScheduleDayOfWeek = DayOfWeek.Sunday;
	public const int DefaultBackupScheduleDayOfMonth = 1;
	public const string DefaultBackupRetentionPolicy = "keep-latest";
	public const int DefaultBackupRetentionKeepLatest = 7;
	public const int MinBackupRetentionKeepLatest = 1;
	public const int MaxBackupRetentionKeepLatest = 100;
	public const bool DefaultBackupBeforeHostUpdate = true;
	public const bool DefaultBackupBeforePluginUpdate = true;

	private const int MaxAdbDeviceSerialLength = 128;

	private static readonly string[] _validThemeModes = ["light", "dark", "system"];

	private readonly IAppPreferenceRepository _repository;
	private readonly IBuildEnvironment _buildEnvironment;
	private readonly IHostListenerState _listenerState;
	private readonly IPublicTlsCertificateStore? _certificateStore;

	public AppPreferenceService(IAppPreferenceRepository repository,
		IBuildEnvironment buildEnvironment,
		IHostListenerState listenerState,
		IPublicTlsCertificateStore? certificateStore = null)
	{
		_repository = repository;
		_buildEnvironment = buildEnvironment;
		_listenerState = listenerState;
		_certificateStore = certificateStore;
	}

	public async Task<AppearanceSettings> GetAppearance()
	{
		var themeMode = (await _repository.GetByKey(ThemeModeKey))?.Value;
		var accentColor = (await _repository.GetByKey(AccentColorKey))?.Value;
		var fontFamily = (await _repository.GetByKey(FontFamilyKey))?.Value;

		return new AppearanceSettings(NormalizeThemeMode(themeMode),
			NormalizeAccentColor(accentColor),
			fontFamily?.Trim() ?? string.Empty);
	}

	public async Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor, string? fontFamily = null)
	{
		var resolved = new AppearanceSettings(NormalizeThemeMode(themeMode),
			NormalizeAccentColor(accentColor),
			fontFamily?.Trim() ?? (await GetAppearance()).FontFamily);

		await _repository.SetValue(ThemeModeKey, resolved.ThemeMode);
		await _repository.SetValue(AccentColorKey, resolved.AccentColor);
		await _repository.SetValue(FontFamilyKey, resolved.FontFamily);

		return resolved;
	}

	public async Task<Guid> GetInstallationId()
	{
		var stored = (await _repository.GetByKey(InstallationIdKey))?.Value;
		if (Guid.TryParse(stored, out var existing) && existing != Guid.Empty)
		{
			return existing;
		}

		var created = Guid.NewGuid();
		await _repository.SetValue(InstallationIdKey, created.ToString("D"));

		return created;
	}

	public async Task<LoggingSettings> GetLogging()
	{
		var stored = (await _repository.GetByKey(MinimumLogLevelKey))?.Value;
		var channelDefault = LogLevelDefaults.ForChannel(_buildEnvironment.Channel);

		return new LoggingSettings(NormalizeLogLevel(stored, channelDefault), channelDefault);
	}

	public async Task<LoggingSettings> SetLogging(LogEntryLevel? minimumLevel)
	{
		var channelDefault = LogLevelDefaults.ForChannel(_buildEnvironment.Channel);
		var resolved = new LoggingSettings(
			minimumLevel is { } requested && Enum.IsDefined(requested) ? requested : channelDefault,
			channelDefault);

		await _repository.SetValue(MinimumLogLevelKey, resolved.MinimumLevel.ToString());

		return resolved;
	}

	public async Task<NetworkSettings> GetNetwork()
	{
		var storedPort = (await _repository.GetByKey(PublicPortKey))?.Value;
		var storedTlsEnabled = (await _repository.GetByKey(TlsEnabledKey))?.Value;
		var storedTlsMode = (await _repository.GetByKey(TlsModeKey))?.Value;
		var storedTlsHttpsPort = (await _repository.GetByKey(TlsHttpsPortKey))?.Value;
		var storedDiscoveryEnabled = (await _repository.GetByKey(DiscoveryEnabledKey))?.Value;

		return BuildNetworkSettings(NormalizePublicPort(storedPort),
			NormalizeTlsEnabled(storedTlsEnabled),
			PublicTlsSelector.ParseMode(storedTlsMode),
			NormalizeTlsHttpsPort(storedTlsHttpsPort),
			NormalizeFlag(storedDiscoveryEnabled, true));
	}

	public async Task<NetworkSettings> SetNetwork(int? publicPort,
		bool? tlsEnabled = null,
		string? tlsMode = null,
		int? tlsHttpsPort = null,
		bool? discoveryEnabled = null)
	{
		var current = await GetNetwork();
		var resolvedDiscoveryEnabled = discoveryEnabled ?? current.DiscoveryEnabled;

		var resolvedPort = NormalizePublicPort(publicPort?.ToString(CultureInfo.InvariantCulture));
		var resolvedTlsEnabled = tlsEnabled ?? PublicTlsSelector.DefaultTlsEnabled;
		var resolvedTlsMode = PublicTlsSelector.ParseMode(tlsMode);
		var resolvedTlsHttpsPort = NormalizeTlsHttpsPort(tlsHttpsPort?.ToString(CultureInfo.InvariantCulture));

		// Only the keys that actually changed are written, compared against their normalized current
		// value rather than the raw stored string: a caller that only means to change the port must
		// not also rewrite three untouched TLS keys underneath it just because they were never
		// written before and default to their build default.
		if (resolvedPort != current.PublicPort)
		{
			await _repository.SetValue(PublicPortKey, resolvedPort.ToString(CultureInfo.InvariantCulture));
		}

		if (resolvedTlsEnabled != current.TlsEnabled)
		{
			await _repository.SetValue(TlsEnabledKey, resolvedTlsEnabled.ToString());
		}

		if (resolvedTlsMode != current.TlsMode)
		{
			await _repository.SetValue(TlsModeKey, resolvedTlsMode.ToString());
		}

		if (resolvedTlsHttpsPort != current.TlsHttpsPort)
		{
			await _repository.SetValue(TlsHttpsPortKey, resolvedTlsHttpsPort.ToString(CultureInfo.InvariantCulture));
		}

		if (resolvedDiscoveryEnabled != current.DiscoveryEnabled)
		{
			await _repository.SetValue(DiscoveryEnabledKey, resolvedDiscoveryEnabled.ToString());
		}

		return BuildNetworkSettings(resolvedPort,
			resolvedTlsEnabled,
			resolvedTlsMode,
			resolvedTlsHttpsPort,
			resolvedDiscoveryEnabled);
	}

	public async Task<AdbSettings> GetAdb()
	{
		var enabled = (await _repository.GetByKey(AdbEnabledKey))?.Value;
		var executablePath = (await _repository.GetByKey(AdbExecutablePathKey))?.Value;
		var usbConnectionsEnabled = (await _repository.GetByKey(AdbUsbConnectionsEnabledKey))?.Value;
		var defaultDeviceSerial = (await _repository.GetByKey(AdbDefaultDeviceSerialKey))?.Value;

		return new AdbSettings(NormalizeAdbFlag(enabled),
			NormalizeAdbExecutablePath(executablePath),
			NormalizeAdbFlag(usbConnectionsEnabled, DefaultAdbUsbConnectionsEnabled),
			NormalizeAdbDeviceSerial(defaultDeviceSerial));
	}

	public async Task<AdbSettings> SetAdb(bool? enabled,
		string? executablePath,
		bool? usbConnectionsEnabled,
		string? defaultDeviceSerial)
	{
		var resolved = new AdbSettings(NormalizeAdbFlag(enabled?.ToString()),
			NormalizeAdbExecutablePath(executablePath),
			NormalizeAdbFlag(usbConnectionsEnabled?.ToString(), DefaultAdbUsbConnectionsEnabled),
			NormalizeAdbDeviceSerial(defaultDeviceSerial));

		await _repository.SetValue(AdbEnabledKey, resolved.Enabled.ToString());
		await _repository.SetValue(AdbExecutablePathKey, resolved.ExecutablePath ?? string.Empty);
		await _repository.SetValue(AdbUsbConnectionsEnabledKey, resolved.UsbConnectionsEnabled.ToString());
		await _repository.SetValue(AdbDefaultDeviceSerialKey, resolved.DefaultDeviceSerial ?? string.Empty);

		return resolved;
	}

	public async Task<DeveloperSettings> GetDeveloper()
	{
		var enabled = (await _repository.GetByKey(DeveloperModeKey))?.Value;

		return new DeveloperSettings(NormalizeDeveloperMode(enabled));
	}

	public async Task<DeveloperSettings> SetDeveloper(bool? enabled)
	{
		var resolved = new DeveloperSettings(enabled ?? false);

		await _repository.SetValue(DeveloperModeKey, resolved.Enabled.ToString());

		return resolved;
	}

	public async Task<OnboardingSettings> GetOnboarding()
	{
		var pending = (await _repository.GetByKey(OnboardingPendingKey))?.Value;

		return new OnboardingSettings(NormalizeOnboardingPending(pending));
	}

	public async Task<OnboardingSettings> SetOnboarding(bool? pending)
	{
		var resolved = new OnboardingSettings(pending ?? false);

		await _repository.SetValue(OnboardingPendingKey, resolved.Pending.ToString());

		return resolved;
	}

	public async Task<LockScreenSettings> GetLockScreen()
	{
		var enabled = (await _repository.GetByKey(LockScreenEnabledKey))?.Value;

		return new LockScreenSettings(NormalizeLockScreenEnabled(enabled));
	}

	// Opt-in: an upgrade or a blank store must not start showing a lock screen on clients that
	// never asked for one.
	public async Task<LockScreenSettings> SetLockScreen(bool? enabled)
	{
		var resolved = new LockScreenSettings(enabled ?? false);

		await _repository.SetValue(LockScreenEnabledKey, resolved.Enabled.ToString());

		return resolved;
	}

	public async Task<BackupSettings> GetBackups()
		=> new(NormalizeBackupFrequency((await _repository.GetByKey(BackupScheduleFrequencyKey))?.Value),
			NormalizeTimeOfDay((await _repository.GetByKey(BackupScheduleTimeOfDayKey))?.Value),
			NormalizeDayOfWeek((await _repository.GetByKey(BackupScheduleDayOfWeekKey))?.Value),
			NormalizeDayOfMonth((await _repository.GetByKey(BackupScheduleDayOfMonthKey))?.Value),
			NormalizeRetentionPolicy((await _repository.GetByKey(BackupRetentionPolicyKey))?.Value),
			NormalizeKeepLatest((await _repository.GetByKey(BackupRetentionKeepLatestKey))?.Value),
			NormalizeFlag((await _repository.GetByKey(BackupBeforeHostUpdateKey))?.Value,
				DefaultBackupBeforeHostUpdate),
			NormalizeFlag((await _repository.GetByKey(BackupBeforePluginUpdateKey))?.Value,
				DefaultBackupBeforePluginUpdate));

	public async Task<BackupSettings> SetBackups(string? scheduleFrequency,
		string? scheduleTimeOfDay,
		string? scheduleDayOfWeek,
		int? scheduleDayOfMonth,
		string? retentionPolicy,
		int? retentionKeepLatest,
		bool? beforeHostUpdate,
		bool? beforePluginUpdate)
	{
		var current = await GetBackups();
		var resolved = new BackupSettings(
			scheduleFrequency is null ? current.ScheduleFrequency : NormalizeBackupFrequency(scheduleFrequency),
			scheduleTimeOfDay is null ? current.ScheduleTimeOfDay : NormalizeTimeOfDay(scheduleTimeOfDay),
			scheduleDayOfWeek is null ? current.ScheduleDayOfWeek : NormalizeDayOfWeek(scheduleDayOfWeek),
			scheduleDayOfMonth is null
				? current.ScheduleDayOfMonth
				: NormalizeDayOfMonth(scheduleDayOfMonth.Value.ToString(CultureInfo.InvariantCulture)),
			retentionPolicy is null ? current.RetentionPolicy : NormalizeRetentionPolicy(retentionPolicy),
			retentionKeepLatest is null
				? current.RetentionKeepLatest
				: NormalizeKeepLatest(retentionKeepLatest.Value.ToString(CultureInfo.InvariantCulture)),
			beforeHostUpdate ?? current.BeforeHostUpdate,
			beforePluginUpdate ?? current.BeforePluginUpdate);

		await _repository.SetValue(BackupScheduleFrequencyKey, resolved.ScheduleFrequency);
		await _repository.SetValue(BackupScheduleTimeOfDayKey, resolved.ScheduleTimeOfDay);
		await _repository.SetValue(BackupScheduleDayOfWeekKey, resolved.ScheduleDayOfWeek.ToString());
		await _repository.SetValue(BackupScheduleDayOfMonthKey,
			resolved.ScheduleDayOfMonth.ToString(CultureInfo.InvariantCulture));
		await _repository.SetValue(BackupRetentionPolicyKey, resolved.RetentionPolicy);
		await _repository.SetValue(BackupRetentionKeepLatestKey,
			resolved.RetentionKeepLatest.ToString(CultureInfo.InvariantCulture));
		await _repository.SetValue(BackupBeforeHostUpdateKey, resolved.BeforeHostUpdate.ToString());
		await _repository.SetValue(BackupBeforePluginUpdateKey, resolved.BeforePluginUpdate.ToString());

		return resolved;
	}

	public async Task<ExtensionSettings> GetExtensions()
	{
		var storeEnabled = (await _repository.GetByKey(ExtensionsStoreEnabledKey))?.Value;
		var checkForUpdates = (await _repository.GetByKey(ExtensionsCheckForUpdatesKey))?.Value;
		var notifyOnUpdates = (await _repository.GetByKey(ExtensionsNotifyOnUpdatesKey))?.Value;
		var refreshIntervalMinutes = (await _repository.GetByKey(ExtensionsRefreshIntervalMinutesKey))?.Value;

		return new ExtensionSettings(NormalizeExtensionsFlag(storeEnabled, DefaultExtensionsStoreEnabled),
			NormalizeExtensionsFlag(checkForUpdates, DefaultExtensionsCheckForUpdates),
			NormalizeExtensionsFlag(notifyOnUpdates, DefaultExtensionsNotifyOnUpdates),
			NormalizeRefreshIntervalMinutes(refreshIntervalMinutes));
	}

	public async Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
		bool? checkForUpdates,
		bool? notifyOnUpdates,
		int? refreshIntervalMinutes)
	{
		var resolved = new ExtensionSettings(storeEnabled ?? DefaultExtensionsStoreEnabled,
			checkForUpdates ?? DefaultExtensionsCheckForUpdates,
			notifyOnUpdates ?? DefaultExtensionsNotifyOnUpdates,
			NormalizeRefreshIntervalMinutes(refreshIntervalMinutes?.ToString(CultureInfo.InvariantCulture)));

		await _repository.SetValue(ExtensionsStoreEnabledKey, resolved.StoreEnabled.ToString());
		await _repository.SetValue(ExtensionsCheckForUpdatesKey, resolved.CheckForUpdates.ToString());
		await _repository.SetValue(ExtensionsNotifyOnUpdatesKey, resolved.NotifyOnUpdates.ToString());
		await _repository.SetValue(ExtensionsRefreshIntervalMinutesKey,
			resolved.RefreshIntervalMinutes.ToString(CultureInfo.InvariantCulture));

		return resolved;
	}

	public async Task<LocalizationSettings> GetLocalization()
	{
		var culture = (await _repository.GetByKey(LocalizationCultureKey))?.Value;

		return LocalizationCultureValidation.IsValid(culture)
			? new LocalizationSettings(culture!, false)
			: new LocalizationSettings(SystemCulture(), true);
	}

	// Reported as-is rather than narrowed to a language Macro Deck ships: text falls back to English
	// through the resolver's own chain, while dates, times and numbers still format the way this
	// reader's system does - which stays right even when their language is not translated yet.
	private static string SystemCulture()
	{
		var culture = CultureInfo.CurrentUICulture.Name;
		return LocalizationCultureValidation.IsValid(culture) ? culture : LocalizationDefaults.Culture;
	}

	// Rejected rather than normalized: a garbled culture must not silently overwrite whatever is already
	// stored, unlike ThemeMode/AccentColor above which always have a safe default to fall back to on write.
	public async Task<LocalizationSettings> SetLocalization(string? culture)
	{
		// Blank is the one non-culture that means something: following the system is the absence of a
		// choice, so clearing the stored value is how it is expressed - and it has to be written, not
		// merely skipped, to undo a choice that was made earlier.
		if (string.IsNullOrWhiteSpace(culture))
		{
			await _repository.SetValue(LocalizationCultureKey, string.Empty);
			return await GetLocalization();
		}

		if (!LocalizationCultureValidation.IsValid(culture))
		{
			return await GetLocalization();
		}

		await _repository.SetValue(LocalizationCultureKey, culture);
		return new LocalizationSettings(culture, false);
	}

	public async Task<string> GetTimeFormat()
		=> NormalizeTimeFormat((await _repository.GetByKey(LocalizationTimeFormatKey))?.Value);

	public async Task<string> SetTimeFormat(string? timeFormat)
	{
		var resolved = NormalizeTimeFormat(timeFormat);
		await _repository.SetValue(LocalizationTimeFormatKey, resolved);
		return resolved;
	}

	private static string NormalizeTimeFormat(string? value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			TimeFormat12h => TimeFormat12h,
			TimeFormat24h => TimeFormat24h,
			_ => TimeFormatSystem
		};

	public async Task<DateTimeOffset?> GetBackupScheduleLastRun()
		=> DateTimeOffset.TryParse((await _repository.GetByKey(BackupScheduleLastRunAtKey))?.Value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;

	public Task SetBackupScheduleLastRun(DateTimeOffset value)
		=> _repository.SetValue(BackupScheduleLastRunAtKey, value.ToString("O", CultureInfo.InvariantCulture));

	private static string NormalizeBackupFrequency(string? value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			BackupScheduleDaily => BackupScheduleDaily,
			BackupScheduleWeekly => BackupScheduleWeekly,
			BackupScheduleMonthly => BackupScheduleMonthly,
			_ => BackupScheduleOff
		};

	private static string NormalizeTimeOfDay(string? value)
		=> TimeOnly.TryParseExact(value?.Trim(),
			"HH:mm",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var parsed)
			? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
			: DefaultBackupScheduleTimeOfDay;

	private static DayOfWeek NormalizeDayOfWeek(string? value)
		=> Enum.TryParse<DayOfWeek>(value?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
			? parsed
			: DefaultBackupScheduleDayOfWeek;

	private static int NormalizeDayOfMonth(string? value)
		=> int.TryParse(value?.Trim(), CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 31
			? parsed
			: DefaultBackupScheduleDayOfMonth;

	private static string NormalizeRetentionPolicy(string? value)
	{
		var trimmed = value?.Trim();

		return string.IsNullOrEmpty(trimmed) ? DefaultBackupRetentionPolicy : trimmed;
	}

	private static int NormalizeKeepLatest(string? value)
		=> int.TryParse(value?.Trim(), CultureInfo.InvariantCulture, out var parsed)
			? Math.Clamp(parsed, MinBackupRetentionKeepLatest, MaxBackupRetentionKeepLatest)
			: DefaultBackupRetentionKeepLatest;

	private static bool NormalizeFlag(string? value, bool fallback)
		=> bool.TryParse(value, out var parsed) ? parsed : fallback;

	private NetworkSettings BuildNetworkSettings(int publicPort,
		bool tlsEnabled,
		PublicTlsMode tlsMode,
		int tlsHttpsPort,
		bool discoveryEnabled)
	{
		var certificateInfo = _certificateStore?.ReadInfo();
		var authorityInfo = _certificateStore?.ReadAuthorityInfo();
		var now = DateTimeOffset.UtcNow;
		var activeEndpoints = _listenerState.PublicEndpoints;

		return new NetworkSettings(publicPort,
			BuildConfig.DefaultPublicPort,
			_listenerState.PublicPort,
			_listenerState.PublicPortOverriddenByEnvironment,
			_listenerState.RefusedConfiguredPublicPort == publicPort,
			!_listenerState.PublicListenerAvailable,
			tlsEnabled,
			tlsMode,
			tlsHttpsPort,
			BuildConfig.DefaultPublicHttpsPort,
			activeEndpoints.HttpsPort is not null,
			activeEndpoints.TlsMode,
			activeEndpoints.HttpsPort,
			_listenerState.TlsFailure,
			_listenerState.TlsRejection,
			certificateInfo is not null,
			certificateInfo?.Source,
			certificateInfo?.Subject,
			certificateInfo?.Fingerprint,
			certificateInfo?.NotBefore,
			certificateInfo?.NotAfter,
			certificateInfo is not null && certificateInfo.IsExpired(now),
			certificateInfo is not null && certificateInfo.IsNotYetValid(now),
			_listenerState.ActiveCertificateFingerprint,
			certificateInfo?.Source == PublicTlsCertificateSource.LocalCa,
			authorityInfo is not null,
			authorityInfo?.Subject,
			authorityInfo?.Fingerprint,
			authorityInfo?.NotBefore,
			authorityInfo?.NotAfter,
			discoveryEnabled);
	}

	private static int NormalizePublicPort(string? value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
			PublicPortSelector.IsConfigurable(port)
				? port
				: BuildConfig.DefaultPublicPort;

	// Shares PublicTlsSelector's default so the reported configuration matches the listeners that were
	// actually opened. Two defaults here would make a fresh install report HTTPS as off while serving it,
	// which NetworkListenerIdentity reads as a pending change and turns into a restart prompt that
	// restarting cannot clear.
	private static bool NormalizeTlsEnabled(string? value) => PublicTlsSelector.IsTlsEnabled(value);

	private static int NormalizeTlsHttpsPort(string? value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
			PublicPortSelector.IsConfigurable(port)
				? port
				: BuildConfig.DefaultPublicHttpsPort;

	// Developer Mode defaults to off: it is the kill switch for interactive plugin pairing, so an
	// upgrade or a blank store must not turn it on by itself.
	private static bool NormalizeDeveloperMode(string? value)
		=> bool.TryParse(value, out var parsed) && parsed;

	private static bool NormalizeOnboardingPending(string? value)
		=> bool.TryParse(value, out var parsed) && parsed;

	private static bool NormalizeLockScreenEnabled(string? value)
		=> bool.TryParse(value, out var parsed) && parsed;

	private static bool NormalizeExtensionsFlag(string? value, bool fallback)
		=> bool.TryParse(value, out var parsed) ? parsed : fallback;

	private static int NormalizeRefreshIntervalMinutes(string? value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
			? Math.Clamp(minutes, MinExtensionsRefreshIntervalMinutes, MaxExtensionsRefreshIntervalMinutes)
			: DefaultExtensionsRefreshIntervalMinutes;

	private static string NormalizeThemeMode(string? value)
		=> value is not null && _validThemeModes.Contains(value) ? value : DefaultThemeMode;

	private static LogEntryLevel NormalizeLogLevel(string? value, LogEntryLevel fallback)
		=> Enum.TryParse<LogEntryLevel>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
			? parsed
			: fallback;

	private static string NormalizeAccentColor(string? value)
		=> value is not null && HexColorRegex().IsMatch(value) ? value : DefaultAccentColor;

	// Both adb flags default to off: adb spawns external processes and, once USB connections are
	// enabled, opens a reverse tunnel, so an upgrade or a blank store must not turn either on by itself.
	private static bool NormalizeAdbFlag(string? value, bool fallback = false)
		=> bool.TryParse(value, out var parsed) ? parsed : fallback;

	private static string? NormalizeAdbExecutablePath(string? value)
	{
		var trimmed = value?.Trim();
		return string.IsNullOrEmpty(trimmed) ? null : trimmed;
	}

	private static string? NormalizeAdbDeviceSerial(string? value)
	{
		var trimmed = value?.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			return null;
		}

		return trimmed.Length <= MaxAdbDeviceSerialLength && AdbDeviceSerialRegex().IsMatch(trimmed) ? trimmed : null;
	}

	[GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
	private static partial Regex HexColorRegex();

	[GeneratedRegex(@"^[A-Za-z0-9._:\-]+$")]
	private static partial Regex AdbDeviceSerialRegex();
}

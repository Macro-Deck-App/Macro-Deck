using MacroDeck.Localization;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>The real resolver over the host's own catalogs, so a test sees the same text a client
/// would: literals pass through unchanged and a host key resolves to its authored string.</summary>
internal static class TestLocalization
{
	public static ILocalizationCatalogRegistry Catalogs { get; } = BuildCatalogs();

	public static ILocalizationResolver Resolver { get; } = new LocalizationResolver(Catalogs);

	public static IAppPreferenceService Preferences { get; } = new FakeLocalizationPreferences();

	public static IServiceScopeFactory ScopeFactory { get; } = BuildScopeFactory();

	/// <summary>The real sample-text resolver over those catalogs, for a widget provider that draws the
	/// sample the widget picker shows.</summary>
	public static IWidgetSampleTextResolver SampleText { get; } =
		new WidgetSampleTextResolver(Resolver, ScopeFactory);

	public static string? Resolve(LocalizedText text, string? culture = null) => Resolver.Resolve(text, culture);

	public static string? Resolve(LocalizedText? text, string? culture = null)
		=> text is { } value ? Resolver.Resolve(value, culture) : null;

	public static string Resolve(LocalizedString value, string? culture = null) => Resolver.Resolve(value, culture);

	private static IServiceScopeFactory BuildScopeFactory()
	{
		var services = new ServiceCollection();
		services.AddSingleton(Resolver);
		services.AddSingleton(Preferences);
		return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
	}

	private static LocalizationCatalogRegistry BuildCatalogs()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(MacroDeckStrings.LocalizationCatalog);
		registry.Register(AppStrings.LocalizationCatalog);
		return registry;
	}
}

/// <summary>An <see cref="IAppPreferenceService" /> that answers the active culture and nothing else:
/// every other preference throws, so a test that reaches one by accident fails loudly instead of
/// reading a default that was never meant to stand in for anything.</summary>
internal sealed class FakeLocalizationPreferences : IAppPreferenceService
{
	public string Culture { get; set; } = "en";

	public Task<LocalizationSettings> GetLocalization() => Task.FromResult(new LocalizationSettings(Culture));

	public Task<LocalizationSettings> SetLocalization(string? culture)
	{
		Culture = culture ?? "en";
		return Task.FromResult(new LocalizationSettings(Culture));
	}

	public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

	public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor)
		=> throw new NotSupportedException();

	public Task<Guid> GetInstallationId() => throw new NotSupportedException();

	public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

	public Task<LoggingSettings> SetLogging(LogEntryLevel? minimumLevel) => throw new NotSupportedException();

	public Task<NetworkSettings> GetNetwork() => throw new NotSupportedException();

	public Task<NetworkSettings> SetNetwork(int? publicPort,
		bool? tlsEnabled = null,
		string? tlsMode = null,
		int? tlsHttpsPort = null)
		=> throw new NotSupportedException();

	public Task<AdbSettings> GetAdb() => throw new NotSupportedException();

	public Task<AdbSettings> SetAdb(bool? enabled,
		string? executablePath,
		bool? usbConnectionsEnabled,
		string? defaultDeviceSerial)
		=> throw new NotSupportedException();

	public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

	public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

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

	public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

	public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();
}

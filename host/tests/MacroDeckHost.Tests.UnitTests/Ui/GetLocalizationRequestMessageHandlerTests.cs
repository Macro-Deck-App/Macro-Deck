using MacroDeck.Localization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;

namespace MacroDeckHost.Tests.UnitTests.Ui;

public class GetLocalizationRequestMessageHandlerTests
{
	private sealed class NoSystemHourCycle : ISystemHourCycleReader
	{
		public string? Read() => null;
	}

	private sealed class FakeLocalizationPreferenceService : IAppPreferenceService
	{
		public string Culture { get; set; } = "en";

		public Task<LocalizationSettings> GetLocalization() => Task.FromResult(new LocalizationSettings(Culture));

		public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

		public Task<string> GetTimeFormat() => Task.FromResult(AppPreferenceService.TimeFormatSystem);

		public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();

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
	}

	private static LocalizationCatalog PluginCatalog(string scope, IReadOnlyDictionary<string, string> entries)
		=> new(scope,
			"en",
			new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
			{
				["en"] = entries
			});

	[Test]
	public async Task
		The_response_includes_the_core_scope_and_a_registered_plugin_scope_resolved_for_the_active_culture()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);

		var pluginScope = LocalizationScope.ForPlugin("com.example.spotify");
		catalogs.Register(PluginCatalog(pluginScope,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["Connect"] = "Verbunden" }));

		var handler = new GetLocalizationRequestMessageHandler(new FakeLocalizationPreferenceService(),
			catalogs,
			new LocalizationResolver(catalogs),
			new TimeFormatResolver(new FakeLocalizationPreferenceService(), new NoSystemHourCycle()));

		var response = await handler.Handle(new GetLocalizationRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Culture, Is.EqualTo("en"));
			Assert.That(response.FallbackCulture, Is.EqualTo(LocalizationDefaults.Culture));
			Assert.That(response.Translations, Contains.Key($"{LocalizationScope.MacroDeck}:Common.Save"));
			Assert.That(response.Translations[$"{LocalizationScope.MacroDeck}:Common.Save"], Is.EqualTo("Save"));
			Assert.That(response.Translations, Contains.Key($"{pluginScope}:Connect"));
			Assert.That(response.Translations[$"{pluginScope}:Connect"], Is.EqualTo("Verbunden"));
		});
	}

	[Test]
	public async Task A_key_only_one_plugin_has_does_not_leak_into_another_plugins_scope()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);

		var scopeA = LocalizationScope.ForPlugin("com.example.a");
		var scopeB = LocalizationScope.ForPlugin("com.example.b");

		catalogs.Register(PluginCatalog(scopeA,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["OnlyA"] = "Only in A" }));
		catalogs.Register(PluginCatalog(scopeB,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["OnlyB"] = "Only in B" }));

		var handler = new GetLocalizationRequestMessageHandler(new FakeLocalizationPreferenceService(),
			catalogs,
			new LocalizationResolver(catalogs),
			new TimeFormatResolver(new FakeLocalizationPreferenceService(), new NoSystemHourCycle()));

		var response = await handler.Handle(new GetLocalizationRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Translations, Contains.Key($"{scopeA}:OnlyA"));
			Assert.That(response.Translations, Contains.Key($"{scopeB}:OnlyB"));
			Assert.That(response.Translations, Does.Not.ContainKey($"{scopeB}:OnlyA"));
			Assert.That(response.Translations, Does.Not.ContainKey($"{scopeA}:OnlyB"));
		});
	}
}

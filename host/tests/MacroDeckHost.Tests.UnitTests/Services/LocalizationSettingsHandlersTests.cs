using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

using AuthTestFakes = Auth;

public class LocalizationSettingsHandlersTests
{
	private sealed class FakeAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private sealed class CountingAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public int Writes { get; private set; }

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			Writes++;
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}

		public void Seed(string key, string value) =>
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private static AppPreferenceService CreateService(IAppPreferenceRepository repository)
		=> new(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

	[Test]
	public async Task The_culture_survives_a_restart()
	{
		var repository = new FakeAppPreferenceRepository();
		var service = CreateService(repository);

		await service.SetLocalization("de-DE");

		// A fresh service built over the same store stands in for a host restart: nothing about the
		// culture may live only in the first instance's memory.
		var rebuilt = CreateService(repository);
		var reloaded = await rebuilt.GetLocalization();

		Assert.That(reloaded.Culture, Is.EqualTo("de-DE"));
	}

	[Test]
	public async Task An_invalid_culture_is_rejected_without_writing_anything()
	{
		var repository = new CountingAppPreferenceRepository();
		repository.Seed(AppPreferenceService.LocalizationCultureKey, "de-DE");
		var service = CreateService(repository);

		var result = await service.SetLocalization("not a real culture!!!");
		var reloaded = await service.GetLocalization();

		Assert.Multiple(() =>
		{
			Assert.That(result.Culture, Is.EqualTo("de-DE"));
			Assert.That(reloaded.Culture, Is.EqualTo("de-DE"));
			Assert.That(repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task Get_handler_returns_persisted_culture()
	{
		var repository = new FakeAppPreferenceRepository();
		var service = CreateService(repository);
		await service.SetLocalization("de-DE");
		var handler = new GetLocalizationSettingsRequestMessageHandler(service);

		var response = await handler.Handle(new GetLocalizationSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Culture, Is.EqualTo("de-DE"));
			Assert.That(response.FallbackCulture, Is.EqualTo(LocalizationDefaults.Culture));
		});
	}

	// The operating system's language is real ambient state, so a test that says anything about it has
	// to set it - otherwise these pass or fail depending on the machine they run on.
	private static async Task WithSystemCulture(string culture, Func<Task> body)
	{
		var previous = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
		try
		{
			await body();
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Test]
	public Task A_fresh_install_follows_the_operating_systems_language()
		=> WithSystemCulture("fr-FR",
			async () =>
			{
				var service = CreateService(new FakeAppPreferenceRepository());
				var handler = new GetLocalizationSettingsRequestMessageHandler(service);

				var response = await handler.Handle(new GetLocalizationSettingsRequest(), CancellationToken.None);

				Assert.Multiple(() =>
				{
					Assert.That(response.Culture, Is.EqualTo("fr-FR"));
					Assert.That(response.FollowSystem, Is.True);
					Assert.That(response.FallbackCulture, Is.EqualTo(LocalizationDefaults.Culture));
				});
			});

	[Test]
	public Task A_chosen_language_stays_put_when_the_system_language_changes()
		=> WithSystemCulture("fr-FR",
			async () =>
			{
				var repository = new FakeAppPreferenceRepository();
				await CreateService(repository).SetLocalization("de-DE");

				await WithSystemCulture("ja-JP",
					async () =>
					{
						var reloaded = await CreateService(repository).GetLocalization();

						Assert.Multiple(() =>
						{
							Assert.That(reloaded.Culture, Is.EqualTo("de-DE"));
							Assert.That(reloaded.FollowSystem, Is.False);
						});
					});
			});

	[Test]
	public Task Clearing_the_chosen_language_follows_the_system_again_across_a_restart()
		=> WithSystemCulture("fr-FR",
			async () =>
			{
				var repository = new FakeAppPreferenceRepository();
				var service = CreateService(repository);
				await service.SetLocalization("de-DE");

				var cleared = await service.SetLocalization(null);

				// Rebuilt over the same store, because a clear that only lived in memory would come back as
				// German on the next start - the choice has to be undone in storage, not merely overridden.
				var reloaded = await CreateService(repository).GetLocalization();

				Assert.Multiple(() =>
				{
					Assert.That(cleared.Culture, Is.EqualTo("fr-FR"));
					Assert.That(cleared.FollowSystem, Is.True);
					Assert.That(reloaded.Culture, Is.EqualTo("fr-FR"));
					Assert.That(reloaded.FollowSystem, Is.True);
				});
			});

	[Test]
	public Task Update_handler_accepts_a_request_to_follow_the_system_without_a_culture()
		=> WithSystemCulture("fr-FR",
			async () =>
			{
				var service = CreateService(new FakeAppPreferenceRepository());
				await service.SetLocalization("de-DE");
				var updateHandler = new UpdateLocalizationSettingsRequestMessageHandler(service,
					new FakeIntegrationRegistrar(),
					new RecordingMediator());

				var response = await updateHandler.Handle(new UpdateLocalizationSettingsRequest { FollowSystem = true },
					CancellationToken.None);

				Assert.Multiple(() =>
				{
					Assert.That(response.Success, Is.True);
					Assert.That(response.Culture, Is.EqualTo("fr-FR"));
					Assert.That(response.FollowSystem, Is.True);
				});
			});

	[Test]
	public async Task Update_handler_persists_and_echoes_the_applied_culture()
	{
		var service = CreateService(new FakeAppPreferenceRepository());
		var updateHandler =
			new UpdateLocalizationSettingsRequestMessageHandler(service,
				new FakeIntegrationRegistrar(),
				new RecordingMediator());
		var getHandler = new GetLocalizationSettingsRequestMessageHandler(service);

		var updated =
			await updateHandler.Handle(new UpdateLocalizationSettingsRequest { Culture = "de-DE" },
				CancellationToken.None);
		var reloaded = await getHandler.Handle(new GetLocalizationSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.Success, Is.True);
			Assert.That(updated.Culture, Is.EqualTo("de-DE"));
			Assert.That(reloaded.Culture, Is.EqualTo("de-DE"));
		});
	}

	[Test]
	public async Task Update_handler_rejects_an_invalid_culture_without_writing_it()
	{
		var repository = new CountingAppPreferenceRepository();
		repository.Seed(AppPreferenceService.LocalizationCultureKey, "de-DE");
		var service = CreateService(repository);
		var updateHandler =
			new UpdateLocalizationSettingsRequestMessageHandler(service,
				new FakeIntegrationRegistrar(),
				new RecordingMediator());

		var response = await updateHandler.Handle(new UpdateLocalizationSettingsRequest { Culture = "nope!!!" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(response.Culture, Is.EqualTo("de-DE"));
			Assert.That(repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task A_connected_client_observes_the_culture_changed_event_when_the_language_changes()
	{
		var service = CreateService(new FakeAppPreferenceRepository());
		var mediator = new RecordingMediator();
		var updateHandler
			= new UpdateLocalizationSettingsRequestMessageHandler(service, new FakeIntegrationRegistrar(), mediator);
		var transport = new AuthTestFakes.RecordingUiTransport();
		var notificationHandler = new LocalizationCultureChangedNotificationHandler(transport);

		await updateHandler.Handle(new UpdateLocalizationSettingsRequest { Culture = "de-DE" }, CancellationToken.None);

		// The observable outcome the brief cares about is what a connected client sees, not how many
		// times something was published internally - so the notification the handler actually produced
		// is fed through the real handler that owns delivery, and the assertion is on what arrived at
		// the transport.
		var published = mediator.Published.OfType<LocalizationCultureChangedNotification>().Single();
		await notificationHandler.Handle(published, CancellationToken.None);

		var received = transport.Sent.OfType<LocalizationCultureChangedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(received.Culture, Is.EqualTo("de-DE"));
			Assert.That(received.FallbackCulture, Is.EqualTo(LocalizationDefaults.Culture));
		});
	}

	private sealed class FixedHourCycleReader(string? hourCycle) : ISystemHourCycleReader
	{
		public string? Read() => hourCycle;
	}

	private static async Task<TimeFormatResolver> Resolver(string culture, string timeFormat, string? systemHourCycle)
	{
		var service = CreateService(new FakeAppPreferenceRepository());
		await service.SetLocalization(culture);
		await service.SetTimeFormat(timeFormat);
		return new TimeFormatResolver(service, new FixedHourCycleReader(systemHourCycle));
	}

	[Test]
	public async Task The_time_format_survives_a_restart_and_an_unknown_value_reads_as_system()
	{
		var repository = new FakeAppPreferenceRepository();
		await CreateService(repository).SetTimeFormat("12h");
		var reloaded = await CreateService(repository).GetTimeFormat();

		await repository.SetValue(AppPreferenceService.LocalizationTimeFormatKey, "36h");
		var garbled = await CreateService(repository).GetTimeFormat();

		Assert.Multiple(() =>
		{
			Assert.That(reloaded, Is.EqualTo("12h"));
			Assert.That(garbled, Is.EqualTo("system"));
		});
	}

	[TestCase("12h", HourCycles.H23, HourCycles.H12)]
	[TestCase("24h", HourCycles.H12, HourCycles.H23)]
	[TestCase("system", HourCycles.H12, HourCycles.H12)]
	[TestCase("system", HourCycles.H23, HourCycles.H23)]
	public async Task A_pinned_time_format_wins_and_system_follows_the_operating_system(string timeFormat,
		string systemHourCycle,
		string expected)
	{
		var resolver = await Resolver("de-DE", timeFormat, systemHourCycle);

		Assert.That(await resolver.ResolveHourCycle(), Is.EqualTo(expected));
	}

	[TestCase("en-US", HourCycles.H12)]
	[TestCase("de-DE", HourCycles.H23)]
	public async Task Without_an_operating_system_setting_the_app_language_decides(string culture, string expected)
	{
		var resolver = await Resolver(culture, "system", null);

		Assert.That(await resolver.ResolveHourCycle(), Is.EqualTo(expected));
	}

	[TestCase("HH 'h' mm", HourCycles.H23)]
	[TestCase("h:mm tt", HourCycles.H12)]
	[TestCase("h:mm 'H'", HourCycles.H12)]
	[TestCase("mm:ss", null)]
	public void An_hour_cycle_is_read_from_a_time_pattern(string pattern, string? expected)
		=> Assert.That(HourCycles.FromPattern(pattern), Is.EqualTo(expected));

	[Test]
	public void A_twelve_hour_time_drops_the_leading_zero_of_a_padded_twenty_four_hour_language()
		=> Assert.That(new TimeOfDayFormat(CultureInfo.GetCultureInfo("de-DE"), HourCycles.H12)
				.Format(new TimeOnly(21, 5)),
			Does.Match(@"^9:05\s\S+$"));

	[Test]
	public async Task Times_are_formatted_in_the_app_language_not_the_process_language()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
		try
		{
			var twelveHour = await (await Resolver("en-US", "12h", null)).ResolveAsync();
			var twentyFourHour = await (await Resolver("es-ES", "24h", null)).ResolveAsync();

			Assert.Multiple(() =>
			{
				Assert.That(twelveHour.Format(new TimeOnly(9, 5)), Does.Match(@"^9:05\sAM$"));
				Assert.That(twentyFourHour.Format(new TimeOnly(9, 5)), Is.EqualTo("9:05"));
				Assert.That(twentyFourHour.Format(new TimeOnly(21, 5)), Is.EqualTo("21:05"));
			});
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Test]
	public async Task A_time_format_only_update_keeps_the_language_and_is_persisted()
	{
		var service = CreateService(new FakeAppPreferenceRepository());
		await service.SetLocalization("de-DE");
		var mediator = new RecordingMediator();
		var updateHandler =
			new UpdateLocalizationSettingsRequestMessageHandler(service, new FakeIntegrationRegistrar(), mediator);

		var response = await updateHandler.Handle(new UpdateLocalizationSettingsRequest { TimeFormat = "12h" },
			CancellationToken.None);
		var reloaded = await new GetLocalizationSettingsRequestMessageHandler(service)
			.Handle(new GetLocalizationSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Culture, Is.EqualTo("de-DE"));
			Assert.That(response.TimeFormat, Is.EqualTo("12h"));
			Assert.That(reloaded.Culture, Is.EqualTo("de-DE"));
			Assert.That(reloaded.FollowSystem, Is.False);
			Assert.That(reloaded.TimeFormat, Is.EqualTo("12h"));
			Assert.That(mediator.Published.OfType<LocalizationCultureChangedNotification>(), Has.Exactly(1).Items);
		});
	}

	[Test]
	public async Task An_unknown_time_format_is_rejected_and_keeps_the_stored_one()
	{
		var service = CreateService(new FakeAppPreferenceRepository());
		await service.SetTimeFormat("12h");
		var mediator = new RecordingMediator();
		var updateHandler =
			new UpdateLocalizationSettingsRequestMessageHandler(service, new FakeIntegrationRegistrar(), mediator);

		var response = await updateHandler.Handle(new UpdateLocalizationSettingsRequest { TimeFormat = "36h" },
			CancellationToken.None);
		var stored = await service.GetTimeFormat();

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.TimeFormat, Is.EqualTo("12h"));
			Assert.That(stored, Is.EqualTo("12h"));
			Assert.That(mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_update_succeeds_without_changing_anything()
	{
		var repository = new CountingAppPreferenceRepository();
		repository.Seed(AppPreferenceService.LocalizationCultureKey, "de-DE");
		repository.Seed(AppPreferenceService.LocalizationTimeFormatKey, "24h");
		var updateHandler = new UpdateLocalizationSettingsRequestMessageHandler(CreateService(repository),
			new FakeIntegrationRegistrar(),
			new RecordingMediator());

		var response = await updateHandler.Handle(new UpdateLocalizationSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Culture, Is.EqualTo("de-DE"));
			Assert.That(response.TimeFormat, Is.EqualTo("24h"));
			Assert.That(repository.Writes, Is.Zero);
		});
	}
}

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
}

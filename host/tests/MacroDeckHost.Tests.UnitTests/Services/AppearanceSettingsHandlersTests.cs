using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class AppearanceSettingsHandlersTests
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

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private static AppPreferenceService CreateService()
		=> new(new FakeAppPreferenceRepository(), new FakeBuildEnvironment(), new FakeHostListenerState());

	[Test]
	public async Task Get_handler_returns_persisted_settings()
	{
		var service = CreateService();
		await service.SetAppearance("dark", "#10b981");
		var handler = new GetAppearanceSettingsRequestMessageHandler(service);

		var response = await handler.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.ThemeMode, Is.EqualTo("dark"));
			Assert.That(response.AccentColor, Is.EqualTo("#10b981"));
		});
	}

	[Test]
	public async Task Update_handler_persists_and_echoes_applied_values()
	{
		var service = CreateService();
		var updateHandler = new UpdateAppearanceSettingsRequestMessageHandler(service, new RecordingMediator());
		var getHandler = new GetAppearanceSettingsRequestMessageHandler(service);

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "light", AccentColor = "#3b82f6" },
			CancellationToken.None);

		var reloaded = await getHandler.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("light"));
			Assert.That(updated.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(reloaded.ThemeMode, Is.EqualTo("light"));
			Assert.That(reloaded.AccentColor, Is.EqualTo("#3b82f6"));
		});
	}

	[Test]
	public async Task Update_handler_normalizes_invalid_values()
	{
		var service = CreateService();
		var updateHandler = new UpdateAppearanceSettingsRequestMessageHandler(service, new RecordingMediator());

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "neon", AccentColor = "blue" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("system"));
			Assert.That(updated.AccentColor, Is.EqualTo("#2196F3"));
		});
	}

	[Test]
	public async Task Update_handler_broadcasts_the_applied_appearance()
	{
		var service = CreateService();
		var mediator = new RecordingMediator();
		var updateHandler = new UpdateAppearanceSettingsRequestMessageHandler(service, mediator);

		await updateHandler.Handle(new UpdateAppearanceSettingsRequest { ThemeMode = "neon", AccentColor = "#3b82f6" },
			CancellationToken.None);

		var published = mediator.Published.OfType<AppearanceChangedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.ThemeMode, Is.EqualTo("system"));
			Assert.That(published.AccentColor, Is.EqualTo("#3b82f6"));
		});
	}
}

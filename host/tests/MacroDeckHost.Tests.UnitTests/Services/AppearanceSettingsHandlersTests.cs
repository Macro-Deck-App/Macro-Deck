using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Rendering;
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

	private sealed class FakeFontCatalog : IFontCatalog
	{
		public IReadOnlyList<FontFaceInfo> GetFaces() =>
		[
			new("inter-400", "Inter", 400, 5, "upright", "Regular", RemoteRenderable: true),
			new("inter-700", "Inter", 700, 5, "upright", "Bold", RemoteRenderable: true),
			new("locked-400", "Locked", 400, 5, "upright", "Regular", RemoteRenderable: false),
		];

		public byte[]? GetFaceFile(string faceId) => null;
	}

	private static AppPreferenceService CreateService()
		=> new(new FakeAppPreferenceRepository(), new FakeBuildEnvironment(), new FakeHostListenerState());

	private static UpdateAppearanceSettingsRequestMessageHandler CreateUpdateHandler(
		IAppPreferenceService service,
		RecordingMediator? mediator = null)
		=> new(service, mediator ?? new RecordingMediator(), new FakeFontCatalog());

	[Test]
	public async Task Get_handler_returns_persisted_settings()
	{
		var service = CreateService();
		await service.SetAppearance("dark", "#10b981", "Inter");
		var handler = new GetAppearanceSettingsRequestMessageHandler(service);

		var response = await handler.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.ThemeMode, Is.EqualTo("dark"));
			Assert.That(response.AccentColor, Is.EqualTo("#10b981"));
			Assert.That(response.FontFamily, Is.EqualTo("Inter"));
		});
	}

	[Test]
	public async Task Get_handler_reports_the_system_font_when_none_was_ever_chosen()
	{
		var handler = new GetAppearanceSettingsRequestMessageHandler(CreateService());

		var response = await handler.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.That(response.FontFamily, Is.Empty);
	}

	[Test]
	public async Task Update_handler_persists_and_echoes_applied_values()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);
		var getHandler = new GetAppearanceSettingsRequestMessageHandler(service);

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "light", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var reloaded = await getHandler.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("light"));
			Assert.That(updated.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(updated.FontFamily, Is.EqualTo("Inter"));
			Assert.That(reloaded.ThemeMode, Is.EqualTo("light"));
			Assert.That(reloaded.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(reloaded.FontFamily, Is.EqualTo("Inter"));
		});
	}

	[Test]
	public async Task Update_handler_normalizes_invalid_values()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "neon", AccentColor = "blue", FontFamily = "Nope Sans" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("system"));
			Assert.That(updated.AccentColor, Is.EqualTo("#2196F3"));
			Assert.That(updated.FontFamily, Is.Empty);
		});
	}

	[Test]
	public async Task A_family_clients_cannot_download_falls_back_to_the_system_font()
	{
		var updateHandler = CreateUpdateHandler(CreateService());

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Locked" },
			CancellationToken.None);

		Assert.That(updated.FontFamily, Is.Empty);
	}

	[Test]
	public async Task An_empty_font_family_returns_to_the_system_font()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);
		await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "" },
			CancellationToken.None);

		Assert.That(updated.FontFamily, Is.Empty);
	}

	[Test]
	public async Task A_client_that_does_not_know_the_font_setting_keeps_the_chosen_font()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);
		await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var updated = await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "light", AccentColor = "#10b981" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("light"));
			Assert.That(updated.FontFamily, Is.EqualTo("Inter"));
		});
	}

	[Test]
	public async Task Update_handler_broadcasts_the_applied_appearance()
	{
		var service = CreateService();
		var mediator = new RecordingMediator();
		var updateHandler = CreateUpdateHandler(service, mediator);

		await updateHandler.Handle(
			new UpdateAppearanceSettingsRequest { ThemeMode = "neon", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var published = mediator.Published.OfType<AppearanceChangedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.ThemeMode, Is.EqualTo("system"));
			Assert.That(published.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(published.FontFamily, Is.EqualTo("Inter"));
		});
	}
}

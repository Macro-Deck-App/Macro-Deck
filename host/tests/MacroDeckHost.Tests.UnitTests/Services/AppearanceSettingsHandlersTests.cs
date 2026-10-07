using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
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
			new("imported-400", "Imported", 400, 5, "upright", "Regular", RemoteRenderable: true) { UserImported = true },
		];

		public byte[]? GetFaceFile(string faceId) => null;
	}

	private static AppPreferenceService CreateService()
		=> new(new FakeAppPreferenceRepository(), new FakeBuildEnvironment(), new FakeHostListenerState(), TestColors.None);

	private static UpdateAppearanceSettingsRequestMessageHandler CreateUpdateHandler(
		IAppPreferenceService service,
		RecordingMediator? mediator = null)
		=> new(service, mediator ?? new RecordingMediator(), new FakeFontCatalog(), TestColors.None);

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

		var updated = await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "light", AccentColor = "#3b82f6", FontFamily = "Inter" },
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

		var updated = await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "neon", AccentColor = "blue", FontFamily = "Nope Sans" },
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

		var updated = await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Locked" },
			CancellationToken.None);

		Assert.That(updated.FontFamily, Is.Empty);
	}

	[Test]
	public async Task An_imported_family_cannot_become_the_app_font()
	{
		var updateHandler = CreateUpdateHandler(CreateService());

		var updated = await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Imported" },
			CancellationToken.None);

		Assert.That(updated.FontFamily, Is.Empty);
	}

	[Test]
	public async Task An_empty_font_family_returns_to_the_system_font()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);
		await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var updated = await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "" },
			CancellationToken.None);

		Assert.That(updated.FontFamily, Is.Empty);
	}

	[Test]
	public async Task A_client_that_does_not_know_the_font_setting_keeps_the_chosen_font()
	{
		var service = CreateService();
		var updateHandler = CreateUpdateHandler(service);
		await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#3b82f6", FontFamily = "Inter" },
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

		await updateHandler.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "neon", AccentColor = "#3b82f6", FontFamily = "Inter" },
			CancellationToken.None);

		var published = mediator.Published.OfType<AppearanceChangedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.ThemeMode, Is.EqualTo("system"));
			Assert.That(published.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(published.FontFamily, Is.EqualTo("Inter"));
		});
	}

	private const string AccentReference = "{{ vars.brand | color | color_opacity: 50 }}";

	private static (AppPreferenceService Service, UpdateAppearanceSettingsRequestMessageHandler Update,
		RecordingMediator Mediator) WithBrandVariable()
	{
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "brand",
			Scope = VariableScope.Global,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = "#10b981"
		});
		var colors = new ColorReferenceResolver(registry);
		var service = new AppPreferenceService(new FakeAppPreferenceRepository(),
			new FakeBuildEnvironment(),
			new FakeHostListenerState(),
			colors: colors);
		var mediator = new RecordingMediator();
		return (service, new UpdateAppearanceSettingsRequestMessageHandler(service, mediator, new FakeFontCatalog(), colors),
			mediator);
	}

	[Test]
	public async Task An_accent_from_a_color_variable_is_shown_opaque_with_its_reference()
	{
		var (service, update, mediator) = WithBrandVariable();

		var updated = await update.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColorSource = AccentReference },
			CancellationToken.None);
		var reloaded = await new GetAppearanceSettingsRequestMessageHandler(service)
			.Handle(new GetAppearanceSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.AccentColor, Is.EqualTo("#10b981"));
			Assert.That(updated.AccentColorSource, Is.EqualTo(AccentReference));
			Assert.That(reloaded.AccentColor, Is.EqualTo("#10b981"));
			Assert.That(reloaded.AccentColorSource, Is.EqualTo(AccentReference));
			Assert.That(mediator.Published.OfType<AppearanceChangedNotification>().Single().AccentColorSource,
				Is.EqualTo(AccentReference));
		});
	}

	[Test]
	public async Task A_client_that_only_changes_the_theme_keeps_the_accent_reference()
	{
		var (_, update, _) = WithBrandVariable();
		await update.Handle(new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColorSource = AccentReference },
			CancellationToken.None);

		var updated = await update.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "light", AccentColor = "#10B981", FontFamily = "" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.ThemeMode, Is.EqualTo("light"));
			Assert.That(updated.AccentColorSource, Is.EqualTo(AccentReference));
		});
	}

	[Test]
	public async Task A_different_accent_replaces_the_reference()
	{
		var (_, update, _) = WithBrandVariable();
		await update.Handle(new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColorSource = AccentReference },
			CancellationToken.None);

		var updated = await update.Handle(new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColor = "#3b82f6" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.AccentColor, Is.EqualTo("#3b82f6"));
			Assert.That(updated.AccentColorSource, Is.Null);
		});
	}

	[Test]
	public async Task An_explicit_fixed_source_detaches_even_at_the_same_colour()
	{
		var (_, update, _) = WithBrandVariable();
		await update.Handle(new UpdateAppearanceSettingsRequest { ThemeMode = "dark", AccentColorSource = AccentReference },
			CancellationToken.None);

		var updated = await update.Handle(new UpdateAppearanceSettingsRequest
				{ ThemeMode = "dark", AccentColor = "#10b981", AccentColorSource = "#10b981" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.AccentColor, Is.EqualTo("#10b981"));
			Assert.That(updated.AccentColorSource, Is.Null);
		});
	}
}

using System.Text.Json.Nodes;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Events;

[TestFixture]
public class ColorVariableChangedHandlerTests
{
	private const string Reference = "{{ vars.primary | color }}";

	private VariableRegistry _registry = null!;
	private FakeFolderCache _folders = null!;
	private WidgetRenderSignals _signals = null!;
	private RecordingUiSessionBroker _sessions = null!;
	private RecordingUiTransport _transport = null!;
	private RecordingDeviceSurfaces _devices = null!;
	private RecordingMediator _mediator = null!;
	private AppPreferenceService _preferences = null!;
	private WidgetVariableIndex _index = null!;
	private ColorVariableChangedHandler _handler = null!;
	private ServiceProvider _provider = null!;
	private CountingPreferences _store = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_folders = new FakeFolderCache();
		_signals = new WidgetRenderSignals();
		_sessions = new RecordingUiSessionBroker();
		_transport = new RecordingUiTransport();
		_devices = new RecordingDeviceSurfaces();
		_mediator = new RecordingMediator();
		var colors = new ColorReferenceResolver(_registry);
		_store = new CountingPreferences();
		_preferences = new AppPreferenceService(_store, new FakeBuildEnvironment(), new FakeHostListenerState(), colors);
		_provider = new ServiceCollection()
			.AddSingleton<IAppPreferenceService>(_preferences)
			.AddSingleton<IMediator>(_mediator)
			.BuildServiceProvider();
		_index = new WidgetVariableIndex(_folders);
		_handler = new ColorVariableChangedHandler(_index,
			_folders,
			new EmptyProfileCache(),
			_signals,
			_sessions,
			colors,
			_transport,
			_devices,
			new DeviceLayoutConstraintTracker(_provider.GetRequiredService<IServiceScopeFactory>()),
			_provider.GetRequiredService<IServiceScopeFactory>(),
			new ColorChangeSignal());
	}

	[TearDown]
	public void TearDown() => _provider.Dispose();

	[Test]
	public async Task An_open_session_receives_the_new_colour_without_being_rebuilt()
	{
		var primary = Variable("primary", "#3366ff");
		var widget = Widget(WidgetTypeIds.Slider, $$"""{"color":"{{Reference}}"}""");
		WidgetEntity? received = null;
		using var _ = _signals.SubscribeDataChanged(widget.Id.ToString(), entity =>
		{
			received = entity;
			return true;
		});

		await ChangeValue(primary, "#ff000080");

		Assert.Multiple(() =>
		{
			Assert.That(JsonNode.Parse(received!.Data!)!["color"]!.GetValue<string>(), Is.EqualTo("#ff000080"));
			Assert.That(_sessions.InvalidatedWidgets, Is.Empty);
			Assert.That(widget.Data, Does.Contain(Reference), "the stored reference stays as it was");
			Assert.That(_devices.Invalidations, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_widget_whose_session_cannot_take_the_change_is_rebuilt()
	{
		var primary = Variable("primary", "#3366ff");
		var widget = Widget(WidgetTypeIds.Clock, $$"""{"backgroundColor":"{{Reference}}"}""");
		var untouched = Widget(WidgetTypeIds.Clock, """{"backgroundColor":"#3366ff"}""");

		await ChangeValue(primary, "#00ff00");

		Assert.That(_sessions.InvalidatedWidgets, Is.EqualTo(new[] { widget.Id }));
		Assert.That(_sessions.InvalidatedWidgets, Does.Not.Contain(untouched.Id));
	}

	[Test]
	public async Task A_deleted_variable_leaves_its_widgets_on_the_theme_colour()
	{
		var primary = Variable("primary", "#3366ff");
		var widget = Widget(WidgetTypeIds.Gauges, $$"""{"backgroundColor":"{{Reference}}"}""");
		WidgetEntity? received = null;
		using var _ = _signals.SubscribeDataChanged(widget.Id.ToString(), entity =>
		{
			received = entity;
			return true;
		});

		_registry.Remove(primary.Id);
		await _handler.Handle(new VariableDeletedNotification(primary), CancellationToken.None);

		Assert.That(JsonNode.Parse(received!.Data!)!["backgroundColor"]!.GetValue<string>(), Is.Empty);
	}

	[Test]
	public async Task A_widget_variable_only_repaints_its_own_widget()
	{
		var owner = Widget(WidgetTypeIds.Slider, $$"""{"color":"{{Reference}}"}""");
		var other = Widget(WidgetTypeIds.Slider, $$"""{"color":"{{Reference}}"}""");
		var local = Variable("primary", "#3366ff", owner.Id.ToString());

		await ChangeValue(local, "#ff0000");

		Assert.That(_sessions.InvalidatedWidgets, Is.EqualTo(new[] { owner.Id }));
		Assert.That(_sessions.InvalidatedWidgets, Does.Not.Contain(other.Id));
	}

	[Test]
	public async Task A_folder_background_and_the_accent_follow_the_variable()
	{
		var primary = Variable("primary", "#3366ff");
		_folders.GetAllFolders()[0].BackgroundColor = Reference;
		await _preferences.SetAppearance("dark", Reference);
		_index.Rebuild();

		await ChangeValue(primary, "#ff0000");

		Assert.Multiple(() =>
		{
			var folder = _transport.Broadcasts.OfType<FolderUpdatedEvent>().Single().Folder;
			Assert.That(folder.BackgroundColor, Is.EqualTo("#ff0000"));
			Assert.That(folder.BackgroundColorSource, Is.EqualTo(Reference));
			var appearance = _mediator.Published.OfType<AppearanceChangedNotification>().Single();
			Assert.That(appearance.AccentColor, Is.EqualTo("#ff0000"));
			Assert.That(appearance.AccentColorSource, Is.EqualTo(Reference));
			Assert.That(_devices.Invalidations, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_change_nothing_references_does_no_work()
	{
		var primary = Variable("primary", "#3366ff");
		Widget(WidgetTypeIds.Clock, """{"backgroundColor":"{{ vars.primary }}"}""");

		await ChangeValue(primary, "#ff0000");

		Assert.Multiple(() =>
		{
			Assert.That(_sessions.InvalidatedWidgets, Is.Empty);
			Assert.That(_transport.Broadcasts, Is.Empty);
			Assert.That(_devices.Invalidations, Is.Zero);
		});
	}

	[Test]
	public async Task Renaming_a_variable_repaints_what_referenced_its_old_name()
	{
		var widget = Widget(WidgetTypeIds.Clock, """{"backgroundColor":"{{ vars.old_name | color }}"}""");
		var renamed = Variable("primary", "#3366ff");

		await _handler.Handle(new VariableUpdatedNotification(renamed, "old_name"), CancellationToken.None);

		Assert.That(_sessions.InvalidatedWidgets, Is.EqualTo(new[] { widget.Id }));
	}

	[Test]
	public async Task An_accent_that_cannot_reference_the_variable_is_not_read_again()
	{
		var primary = Variable("primary", "#3366ff");
		await _preferences.SetAppearance("dark", "#10b981");

		await ChangeValue(primary, "#ff0000");
		var readsAfterFirst = _store.Reads;
		await ChangeValue(primary, "#00ff00");

		Assert.That(_store.Reads, Is.EqualTo(readsAfterFirst));
	}

	internal static ColorVariableChangedHandler Handler(IWidgetVariableIndex index,
		IFolderCache folders,
		VariableRegistry variables,
		IUiSessionBroker sessions,
		ColorChangeSignal signal)
	{
		var provider = new ServiceCollection()
			.AddSingleton<IAppPreferenceService>(new AppPreferenceService(new CountingPreferences(),
				new FakeBuildEnvironment(),
				new FakeHostListenerState(),
				new ColorReferenceResolver(variables)))
			.AddSingleton<IMediator>(new RecordingMediator())
			.BuildServiceProvider();
		return new ColorVariableChangedHandler(index,
			folders,
			new EmptyProfileCache(),
			new WidgetRenderSignals(),
			sessions,
			new ColorReferenceResolver(variables),
			new RecordingUiTransport(),
			new RecordingDeviceSurfaces(),
			new DeviceLayoutConstraintTracker(provider.GetRequiredService<IServiceScopeFactory>()),
			provider.GetRequiredService<IServiceScopeFactory>(),
			signal);
	}

	private VariableEntity Variable(string name, string value, string? widgetId = null)
	{
		var entity = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = widgetId is null ? VariableScope.Global : VariableScope.Widget,
			ScopeRefId = widgetId,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = value
		};
		_registry.Upsert(entity);
		return entity;
	}

	private WidgetEntity Widget(string type, string data)
	{
		var widget = new WidgetEntity { Id = Guid.NewGuid(), Type = type, Data = data };
		_folders.AddWidget(widget);
		_index.Rebuild();
		return widget;
	}

	private async Task ChangeValue(VariableEntity variable, string value)
	{
		variable.Value = value;
		_registry.Upsert(variable);
		await _handler.Handle(new VariableUpdatedNotification(variable), CancellationToken.None);
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed class RecordingDeviceSurfaces : IDeviceSurfaceService
	{
		public int Invalidations { get; private set; }

		public bool IsOpen(Guid deviceId) => false;

		public Task OpenAsync(Guid deviceId,
			string providerId,
			string providerDeviceId,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task<bool> NavigateAsync(Guid deviceId,
			DeckNavigationCommand command,
			CancellationToken cancellationToken = default) => Task.FromResult(false);

		public Task<DeviceInteractionOutcome> SubmitInteractionAsync(Guid deviceId,
			DeviceInteraction interaction,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.SessionNotFound));

		public Task<DeviceIconImage?> GetIconAsync(Guid deviceId,
			string iconId,
			int? size = null,
			string? knownETag = null,
			CancellationToken cancellationToken = default) => Task.FromResult<DeviceIconImage?>(null);

		public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(Guid deviceId,
			string widgetId,
			string? knownETag = null,
			CancellationToken cancellationToken = default) => Task.FromResult<DeviceWidgetIconImage?>(null);

		public Task InvalidateAsync(CancellationToken cancellationToken = default)
		{
			Invalidations++;
			return Task.CompletedTask;
		}

		public Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class CountingPreferences : MacroDeckHost.Application.Persistence.Repositories.IAppPreferenceRepository
	{
		private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

		public int Reads { get; private set; }

		public Task<AppPreferenceEntity?> GetByKey(string key)
		{
			Reads++;
			return Task.FromResult(_values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);
		}

		public Task SetValue(string key, string value)
		{
			_values[key] = value;
			return Task.CompletedTask;
		}
	}
}

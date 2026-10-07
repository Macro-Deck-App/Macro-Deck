using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class PluginColorWatchesTests
{
	private const string PluginId = "com.example.colors";
	private const string Reference = "{{ vars.primary | color }}";

	private VariableRegistry _variables = null!;
	private FakeFolderCache _folders = null!;
	private PluginSessionRegistry _sessions = null!;
	private ColorChangeSignal _signal = null!;
	private PluginColorWatches _watches = null!;
	private RecordingConnection _connection = null!;
	private WidgetEntity _widget = null!;

	[SetUp]
	public async Task SetUp()
	{
		_variables = new VariableRegistry();
		_folders = new FakeFolderCache();
		_widget = new WidgetEntity { Id = Guid.NewGuid(), Type = WidgetTypeIds.Clock, Data = "{}" };
		_folders.AddWidget(_widget);
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_signal = new ColorChangeSignal();
		_watches = Create(TimeSpan.Zero);
		_connection = await ConnectAsync("session-1");
	}

	[TearDown]
	public void TearDown() => _watches.Dispose();

	[Test]
	public void A_value_resolves_globally_or_in_a_widgets_scope()
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("primary", VariableType.Color, "#ff0000", _widget.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(_watches.Resolve(Reference, null), Is.EqualTo("#3366ff"));
			Assert.That(_watches.Resolve(Reference, _widget.Id.ToString()), Is.EqualTo("#ff0000"));
			Assert.That(_watches.Resolve("#ABC", null), Is.EqualTo("#aabbcc"));
		});
	}

	[TestCase("{{ vars.label | color }}", null)]
	[TestCase("{{ vars.primary }}", null)]
	[TestCase("{{ vars.primary | color }}", "00000000-0000-0000-0000-000000000001")]
	[TestCase("red", null)]
	public void Anything_but_a_colour_or_a_color_variable_resolves_to_nothing(string value, string? widgetId)
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("label", VariableType.Text, "#3366ff");

		Assert.That(_watches.Resolve(value, widgetId), Is.Null);
	}

	[Test]
	public async Task A_new_table_is_pushed_with_the_current_values_and_a_change_pushes_again()
	{
		var primary = Add("primary", VariableType.Color, "#3366ff");

		_watches.TrySetWatches(PluginId, [Watch("a", Reference), Watch("b", "#ffffff")]);
		var first = await NextPushAsync(0);
		primary.Value = "#ff0000";
		_variables.Upsert(primary);
		_signal.Raise();
		var second = await NextPushAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(first.Values.Select(value => (value.WatchId, value.Color)),
				Is.EquivalentTo(new[] { ("a", (string?)"#3366ff"), ("b", "#ffffff") }));
			Assert.That(second.Values.Single(value => value.WatchId == "a").Color, Is.EqualTo("#ff0000"));
			Assert.That(second.Revision, Is.GreaterThan(first.Revision));
		});
	}

	[Test]
	public async Task A_plugin_without_watches_is_never_pushed_to_and_an_unchanged_table_is_not_pushed_again()
	{
		Add("primary", VariableType.Color, "#3366ff");
		_watches.TrySetWatches(PluginId, []);
		_signal.Raise();
		await Task.Delay(100);
		var emptyPushes = _connection.Pushes.Count;

		_watches.TrySetWatches(PluginId, [Watch("a", Reference)]);
		await NextPushAsync(0);
		_signal.Raise();
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(emptyPushes, Is.Zero);
			Assert.That(_connection.Pushes, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_burst_of_changes_ends_in_one_push_with_the_last_value()
	{
		_watches.Dispose();
		_watches = Create(TimeSpan.FromMilliseconds(200));
		var primary = Add("primary", VariableType.Color, "#000000");
		_watches.TrySetWatches(PluginId, [Watch("a", Reference)]);
		await NextPushAsync(0);

		foreach (var value in new[] { "#111111", "#222222", "#333333" })
		{
			primary.Value = value;
			_variables.Upsert(primary);
			_signal.Raise();
		}

		var push = await NextPushAsync(1);
		await Task.Delay(400);

		Assert.Multiple(() =>
		{
			Assert.That(push.Values.Single().Color, Is.EqualTo("#333333"));
			Assert.That(_connection.Pushes, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_watched_widget_going_away_pushes_no_colour()
	{
		Add("primary", VariableType.Color, "#3366ff");
		_watches.TrySetWatches(PluginId, [Watch("a", Reference, _widget.Id.ToString())]);
		await NextPushAsync(0);

		_folders.GetAllFolders()[0].Widgets.Remove(_widget);
		await new ColorWatchWidgetDeletedHandler(_signal)
			.Handle(new WidgetDeletedNotification(_widget.Id, _widget.FolderId), CancellationToken.None);

		Assert.That((await NextPushAsync(1)).Values.Single().Color, Is.Null);
	}

	[Test]
	public async Task Deleting_the_profile_of_a_watched_widget_pushes_no_colour()
	{
		Add("primary", VariableType.Color, "#3366ff");
		_watches.TrySetWatches(PluginId, [Watch("a", Reference, _widget.Id.ToString())]);
		await NextPushAsync(0);

		_folders.GetAllFolders()[0].Widgets.Remove(_widget);
		await new ColorWatchWidgetDeletedHandler(_signal)
			.Handle(new ProfileDeletedNotification(Guid.NewGuid()), CancellationToken.None);

		Assert.That((await NextPushAsync(1)).Values.Single().Color, Is.Null);
	}

	[Test]
	public void More_watches_than_the_limit_are_refused()
		=> Assert.That(_watches.TrySetWatches(PluginId,
				[.. Enumerable.Range(0, ProtocolLimits.MaxColorWatches + 1).Select(i => Watch($"w{i}", "#000000"))]),
			Is.False);

	[Test]
	public async Task The_table_is_dropped_only_when_the_plugins_last_session_ends()
	{
		_watches.TrySetWatches(PluginId, [Watch("a", "#ffffff")]);

		await ConnectAsync("session-2");
		var afterReplace = _watches.WatchesOf(PluginId).Count;
		_sessions.Detach("session-2", DateTimeOffset.UtcNow);

		Assert.Multiple(() =>
		{
			Assert.That(afterReplace, Is.EqualTo(1));
			Assert.That(_watches.WatchesOf(PluginId), Is.Empty);
		});
	}

	[Test]
	public async Task A_restarted_host_pushes_the_current_value_for_the_table_the_plugin_sends_again()
	{
		Add("primary", VariableType.Color, "#3366ff");
		_watches.Dispose();
		_watches = Create(TimeSpan.Zero);

		_watches.TrySetWatches(PluginId, [Watch("a", Reference)]);

		Assert.That((await NextPushAsync(0)).Values.Single().Color, Is.EqualTo("#3366ff"));
	}

	[Test]
	public async Task A_provider_pushed_color_variable_fires_a_watch_and_repaints_a_widget()
	{
		var mediator = new RecordingMediator();
		var service = TestVariableServices.Create(_variables, new NullStore(), mediator);
		var created = await service.CreateIntegrationVariable("com.example.provider",
			"primary",
			VariableScope.Global,
			null,
			VariableType.Color,
			"#3366ff",
			null);
		var widget = new WidgetEntity { Id = Guid.NewGuid(), Type = WidgetTypeIds.Clock, Data = $$"""{"backgroundColor":"{{Reference}}"}""" };
		_folders.AddWidget(widget);
		var index = new WidgetVariableIndex(_folders);
		index.Rebuild();
		var broker = new RecordingUiSessionBroker();
		var handler = Events.ColorVariableChangedHandlerTests.Handler(index, _folders, _variables, broker, _signal);
		mediator.BeforePublish = notification =>
		{
			if (notification is VariableUpdatedNotification updated)
			{
				handler.Handle(updated, CancellationToken.None).AsTask().GetAwaiter().GetResult();
			}
		};
		_watches.TrySetWatches(PluginId, [Watch("a", Reference)]);
		await NextPushAsync(0);

		await service.ReportIntegrationVariableValue("com.example.provider", created.Data!.Id, "#ff0000");

		Assert.Multiple(async () =>
		{
			Assert.That((await NextPushAsync(1)).Values.Single().Color, Is.EqualTo("#ff0000"));
			Assert.That(broker.InvalidatedWidgets, Does.Contain(widget.Id));
		});
	}

	[Test]
	public async Task An_in_process_watch_gets_its_first_value_once_then_changes_until_released()
	{
		var primary = Add("primary", VariableType.Color, "#3366ff");
		var api = new InProcessColorApi(_watches, Serilog.Core.Logger.None);
		var received = new ConcurrentQueue<string?>();
		await api.WatchAsync(Reference, (color, _) =>
		{
			received.Enqueue(color);
			return Task.CompletedTask;
		});
		await WaitForAsync(() => received.Count == 1);

		_signal.Raise();
		primary.Value = "#ff0000";
		_variables.Upsert(primary);
		_signal.Raise();
		await WaitForAsync(() => received.Count == 2);
		await api.DisposeAsync();
		primary.Value = "#00ff00";
		_variables.Upsert(primary);
		_signal.Raise();
		await Task.Delay(100);

		Assert.That(received, Is.EqualTo(new[] { "#3366ff", "#ff0000" }));
	}

	private PluginColorWatches Create(TimeSpan window)
		=> new(new ColorReferenceResolver(_variables),
			_folders,
			_sessions,
			_signal,
			Serilog.Core.Logger.None,
			coalescingWindow: window);

	private async Task<RecordingConnection> ConnectAsync(string sessionId)
	{
		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = PluginId,
			DisplayName = PluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = ProtocolVersions.Current,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = DateTimeOffset.UtcNow
		});
		var connection = new RecordingConnection();
		Assert.That(_sessions.TryAttach(sessionId, connection, null), Is.True);
		return connection;
	}

	private VariableEntity Add(string name, VariableType type, string value, string? widgetId = null)
	{
		var entity = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = widgetId is null ? VariableScope.Global : VariableScope.Widget,
			ScopeRefId = widgetId,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
		_variables.Upsert(entity);
		return entity;
	}

	private static ColorWatchDto Watch(string id, string value, string? widgetId = null)
		=> new() { WatchId = id, Value = value, WidgetId = widgetId };

	private async Task<ColorWatchStateDto> NextPushAsync(int index)
	{
		await WaitForAsync(() => _connection.Pushes.Count > index);
		return _connection.Pushes.ElementAt(index);
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The expected state was never reached.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class RecordingConnection : IPluginConnection
	{
		public ConcurrentQueue<ColorWatchStateDto> Pushes { get; } = new();

		public string ConnectionId { get; } = Guid.NewGuid().ToString();

		public Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
		{
			var payload = envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!;
			if (envelope.Type == MessageTypes.HostState && payload.Api == HostApis.Colors)
			{
				Pushes.Enqueue(payload.Data!.Value.Deserialize<ColorWatchStateDto>(PluginProtocolJson.Options)!);
			}

			return Task.CompletedTask;
		}

		public Task Close(int closeCode, string reason, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class NullStore : MacroDeckHost.Application.Persistence.IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> variables)
		{
		}
	}
}

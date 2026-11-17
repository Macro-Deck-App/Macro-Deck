using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherEventBindingTests
{
	private const string PluginId = "com.hotkeys";

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _connection = null!;
	private CapturingTracker _tracker = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();
		_tracker = new CapturingTracker();
		_pusher = new HostStatePusher(_registry,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			_tracker,
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new Adb.FakeAdbManager(),
			new FixedAdbAccessPolicy(),
			Serilog.Core.Logger.None);

		await Connect("session-1", PluginId, _connection, []);
	}

	private async Task Connect(string sessionId, string pluginId, FakePluginConnection connection, string[] features)
	{
		await _registry.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			HostApiFeatures = features,
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		Assert.That(_registry.TryAttach(sessionId, connection, null), Is.True);
		await _pusher.PushAllAsync(pluginId);
	}

	[TearDown]
	public void DisposeStatePusher()
	{
		_pusher.Dispose();
	}

	[Test]
	public async Task A_change_to_a_connected_plugins_bindings_pushes_its_current_list()
	{
		_tracker.Current = [Binding("hotkey-pressed")];

		await _tracker.Deliver(PluginId, _tracker.Current);

		var pushes = EventBindingPushes();
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(2));
			Assert.That(pushes[^1].Single().EventId, Is.EqualTo("hotkey-pressed"));
		});
	}

	[Test]
	public async Task Another_plugins_binding_change_is_not_pushed_to_this_plugin()
	{
		await _tracker.Deliver("com.other", [Binding("scene-changed")]);

		Assert.That(EventBindingPushes(), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Every_registration_pushes_the_current_bindings_even_when_they_did_not_change()
	{
		_tracker.Current = [Binding("hotkey-pressed")];

		await _pusher.PushAllAsync(PluginId);
		await _pusher.PushAllAsync(PluginId);

		var pushes = EventBindingPushes();
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(3));
			Assert.That(pushes.Skip(1).Select(push => push.Single().EventId), Is.All.EqualTo("hotkey-pressed"));
		});
	}

	[Test]
	public async Task A_plugin_that_did_not_opt_in_sees_no_resolved_value_and_no_push_when_only_it_changed()
	{
		_tracker.Current = [BoundBinding("Intro")];
		await _tracker.Deliver(PluginId, _tracker.Current);

		_tracker.Current = [BoundBinding("Outro")];
		await _tracker.Deliver(PluginId, _tracker.Current);

		var pushes = EventBindingPushes();
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(2));
			Assert.That(pushes[^1].Single().Parameters["scene"].ResolvedValue, Is.Null);
			Assert.That(pushes[^1].Single().Parameters["scene"].Value!.Value.GetProperty("$var").GetString(),
				Is.EqualTo("scene"));
		});
	}

	[Test]
	public async Task A_plugin_that_opted_in_gets_the_resolved_value_and_a_push_when_it_changes()
	{
		const string obs = "com.obs";
		var connection = new FakePluginConnection();
		await Connect("session-2", obs, connection, [HostApiFeatures.EventBindingResolvedValue]);

		_tracker.Current = [BoundBinding("Intro")];
		await _tracker.Deliver(obs, _tracker.Current);
		_tracker.Current = [BoundBinding("Outro")];
		await _tracker.Deliver(obs, _tracker.Current);

		var pushes = EventBindingPushes(connection);
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(3));
			Assert.That(pushes[1].Single().Parameters["scene"].ResolvedValue!.Value.GetString(), Is.EqualTo("Intro"));
			Assert.That(pushes[2].Single().Parameters["scene"].ResolvedValue!.Value.GetString(), Is.EqualTo("Outro"));
		});
	}

	private List<List<EventBindingDto>> EventBindingPushes()
	{
		return EventBindingPushes(_connection);
	}

	private static List<List<EventBindingDto>> EventBindingPushes(FakePluginConnection connection)
	{
		return connection.Sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.EventBindings)
			.Select(payload => payload.Data!.Value.Deserialize<List<EventBindingDto>>(PluginProtocolJson.Options)!)
			.ToList();
	}

	private static EventBindingDto Binding(string eventId)
	{
		return new EventBindingDto { EventId = eventId };
	}

	private static EventBindingDto BoundBinding(string resolved)
	{
		return new EventBindingDto
		{
			EventId = "scene-changed",
			Parameters = new Dictionary<string, EventBindingValueDto>(StringComparer.Ordinal)
			{
				["scene"] = new()
				{
					Value = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$var"] = "scene" }),
					ResolvedValue = JsonSerializer.SerializeToElement(resolved)
				}
			}
		};
	}

	private sealed class CapturingTracker : IEventBindingTracker
	{
		private Func<string, IReadOnlyList<EventBindingDto>, Task>? _listener;

		public IReadOnlyList<EventBindingDto> Current { get; set; } = [];

		public IReadOnlyList<EventBindingDto> BindingsFor(string providerId)
		{
			return Current;
		}

		public IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged)
		{
			_listener = onChanged;
			return new Unsubscriber();
		}

		public Task FlushAsync()
		{
			return Task.CompletedTask;
		}

		public Task Deliver(string providerId, IReadOnlyList<EventBindingDto> bindings)
		{
			return _listener!(providerId, bindings);
		}

		private sealed class Unsubscriber : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}

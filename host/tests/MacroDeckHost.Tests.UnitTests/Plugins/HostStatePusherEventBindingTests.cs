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
		_pusher = new HostStatePusher(_registry, new FakeDeckNavigator(), new FakeScriptApi(), new FakeWidgetApi(), _tracker);

		await _registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Hotkeys",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		Assert.That(_registry.TryAttach("session-1", _connection, null), Is.True);
		await _pusher.PushAllAsync(PluginId);
	}

	[TearDown]
	public void DisposeStatePusher() => _pusher.Dispose();

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

	private List<List<EventBindingDto>> EventBindingPushes()
		=> _connection.Sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.EventBindings)
			.Select(payload => payload.Data!.Value.Deserialize<List<EventBindingDto>>(PluginProtocolJson.Options)!)
			.ToList();

	private static EventBindingDto Binding(string eventId) => new() { EventId = eventId };

	private sealed class CapturingTracker : IEventBindingTracker
	{
		private Func<string, IReadOnlyList<EventBindingDto>, Task>? _listener;

		public IReadOnlyList<EventBindingDto> Current { get; set; } = [];

		public IReadOnlyList<EventBindingDto> BindingsFor(string providerId) => Current;

		public IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged)
		{
			_listener = onChanged;
			return new Unsubscriber();
		}

		public Task FlushAsync() => Task.CompletedTask;

		public Task Deliver(string providerId, IReadOnlyList<EventBindingDto> bindings) => _listener!(providerId, bindings);

		private sealed class Unsubscriber : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}

using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Scripts;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherScriptsTests
{
	private const string OldPlugin = "com.example.old";
	private const string NewPlugin = "com.example.new";

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _old = null!;
	private FakePluginConnection _new = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var scripts = new FakeScriptApi
		{
			Scripts =
			[
				new Script
				{
					Id = "s1",
					Name = "Tint",
					Inputs =
					[
						new ScriptInput { Name = "tint", Type = ScriptInputType.Color, DefaultValue = "#3366ff" },
						new ScriptInput { Name = "count", Type = ScriptInputType.Numeric }
					]
				},
				new Script { Id = "s2", Name = "Plain" }
			]
		};
		_pusher = new HostStatePusher(_registry,
			new FakeDeckNavigator(),
			scripts,
			new FakeWidgetApi(),
			new NoBindings(),
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new MacroDeckHost.Tests.UnitTests.Adb.FakeAdbManager(),
			new FixedAdbAccessPolicy(),
			Serilog.Core.Logger.None);

		_old = await ConnectAsync(OldPlugin, []);
		_new = await ConnectAsync(NewPlugin, [HostApiFeatures.ScriptInputColor]);
	}

	[TearDown]
	public void TearDown() => _pusher.Dispose();

	[Test]
	public async Task A_plugin_that_did_not_negotiate_colour_inputs_gets_the_whole_list_with_text_inputs()
	{
		await _pusher.PushAllAsync(OldPlugin);
		await _pusher.Handle(new ScriptDeletedNotification(Guid.NewGuid()), CancellationToken.None);

		Assert.Multiple(() =>
		{
			foreach (var scripts in ScriptPushes(_old))
			{
				Assert.That(scripts.Select(script => script.GetProperty("id").GetString()), Is.EqualTo(new[] { "s1", "s2" }));
				Assert.That(InputTypes(scripts[0]), Is.EqualTo(new[] { "text", "numeric" }));
				Assert.That(scripts[0].GetProperty("inputs")[0].GetProperty("defaultValue").GetString(), Is.EqualTo("#3366ff"));
			}

			Assert.That(ScriptPushes(_old), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_plugin_that_negotiated_colour_inputs_sees_them()
	{
		await _pusher.PushAllAsync(NewPlugin);
		await _pusher.Handle(new ScriptDeletedNotification(Guid.NewGuid()), CancellationToken.None);

		var pushes = ScriptPushes(_new);
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(2));
			Assert.That(pushes.Select(scripts => InputTypes(scripts[0])),
				Is.All.EqualTo(new[] { "color", "numeric" }));
		});
	}

	private async Task<FakePluginConnection> ConnectAsync(string pluginId, IReadOnlyList<string> features)
	{
		var connection = new FakePluginConnection();
		await _registry.Create(new PluginSessionRecord
		{
			SessionId = pluginId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 3,
			HostApiFeatures = features,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		Assert.That(_registry.TryAttach(pluginId, connection, null), Is.True);
		return connection;
	}

	private static List<List<JsonElement>> ScriptPushes(FakePluginConnection connection)
		=> connection.Sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.Scripts)
			.Select(payload => payload.Data!.Value.EnumerateArray().ToList())
			.ToList();

	private static List<string?> InputTypes(JsonElement script)
		=> [.. script.GetProperty("inputs").EnumerateArray().Select(input => input.GetProperty("type").GetString())];

	private sealed class NoBindings : MacroDeckHost.Application.Triggers.IEventBindingTracker
	{
		public IReadOnlyList<MacroDeck.Plugin.Protocol.Callbacks.EventBindingDto> BindingsFor(string providerId) => [];

		public IDisposable Subscribe(Func<string, IReadOnlyList<MacroDeck.Plugin.Protocol.Callbacks.EventBindingDto>, Task> onChanged)
			=> new Nothing();

		public Task FlushAsync() => Task.CompletedTask;

		private sealed class Nothing : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}

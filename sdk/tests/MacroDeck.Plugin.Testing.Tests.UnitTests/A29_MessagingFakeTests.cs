using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A29_MessagingFakeTests
{
	private static readonly string[] _sceneChanged = ["obs.scene.changed"];

	[Test]
	public async Task The_fake_routes_a_plugins_own_messages_like_the_broker_does()
	{
		var channel = new FakeMessageChannel();
		var events = new List<string>();
		await channel.SubscribeAsync("obs.*", (message, _) =>
		{
			events.Add(message.Topic);
			return Task.CompletedTask;
		});
		await channel.HandleRequestsAsync("obs.scene.current", (_, _) => Task.FromResult<JsonElement?>(Json("Live")));

		await channel.PublishAsync("obs.scene.changed");
		var reply = await channel.RequestAsync("obs.scene.current");

		Assert.Multiple(() =>
		{
			Assert.That(events, Is.EqualTo(_sceneChanged));
			Assert.That(reply?.GetString(), Is.EqualTo("Live"));
			Assert.That(channel.Published.Single().Sender, Is.EqualTo(channel.SenderId));
		});
	}

	[Test]
	public async Task A_stub_answers_what_the_plugin_does_not_handle_and_nothing_answers_the_rest()
	{
		var channel = new FakeMessageChannel();
		channel.RespondTo("lights.state", _ => Json("on"));

		var reply = await channel.RequestAsync("lights.state");
		var exception = Assert.ThrowsAsync<MessageChannelException>(() => channel.SendAsync("lights.toggle"));

		Assert.Multiple(() =>
		{
			Assert.That(reply?.GetString(), Is.EqualTo("on"));
			Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.NoHandler));
			Assert.That(channel.Requested.Single().Topic, Is.EqualTo("lights.state"));
		});
	}

	[Test]
	public async Task In_the_harness_what_an_integration_registers_during_initialization_receives_deliveries()
	{
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => new EchoIntegration()));
		await harness.InitializeIntegrationsAsync();

		var reply = await harness.Context.Messages.DeliverRequestAsync("echo.say", Json("hi"), sender: "com.example.caller");

		Assert.Multiple(() =>
		{
			Assert.That(reply?.GetString(), Is.EqualTo("com.example.caller said hi"));
			Assert.That(harness.Services.GetRequiredService<IMessageChannel>(), Is.SameAs(harness.Context.Messages));
			Assert.That(harness.Declared.Select(capability => capability.Kind), Does.Contain(CapabilityKinds.Messaging));
		});
	}

	[Test]
	public async Task Over_the_wire_the_stub_host_records_what_the_plugin_handles_and_delivers_to_it()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.RegisterIntegration(_ => new EchoIntegration());
		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		var pluginId = plugin.Application.Services.GetRequiredService<PluginMetadata>().Id;

		await Wait.UntilAsync(() => host.Messaging.HandledRequestsOf(pluginId).Contains("echo.say"), TimeSpan.FromSeconds(10));
		var outcome = await session.Messaging.DeliverRequestAsync("echo.say", Json("hi"), sender: "com.example.caller");

		Assert.That(outcome.Data?.GetProperty("payload").GetString(), Is.EqualTo("com.example.caller said hi"));
	}

	private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

	private sealed class EchoIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
			=> await context.Messages.HandleRequestsAsync("echo.say",
				(message, _) => Task.FromResult<JsonElement?>(Json($"{message.Sender} said {message.Payload?.GetString()}")));

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}

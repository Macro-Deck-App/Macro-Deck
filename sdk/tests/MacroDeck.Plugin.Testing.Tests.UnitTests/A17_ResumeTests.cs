using System.Net.WebSockets;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A17 - <c>Resumed</c> is true only for a genuine resume, proven by an integration-owned counter that
/// only a real re-initialization can move, not just the host's own bookkeeping.
/// </summary>
[TestFixture]
public class A17_ResumeTests
{
	[Test]
	public async Task A_non_fatal_close_resumes_the_same_session_without_re_initializing()
	{
		var integration = new TestIntegration("test.a17.a");

		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => integration);

		await using var plugin = await host.HostAsync(builder);
		var first = await host.WaitForSessionAsync();

		await WaitForInitializeCount(integration, 1);

		await host.DisconnectAsync((int)WebSocketCloseStatus.NormalClosure);

		var second = await host.WaitForSessionAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(second.SessionId, Is.EqualTo(first.SessionId));
			Assert.That(second.Resumed, Is.True);
			Assert.That(host.DeletedSessions, Does.Not.Contain(first.SessionId));
		});

		// A resumed session needs no re-initialization - the counter proves the plugin agrees, not just the host.
		await Task.Delay(TimeSpan.FromMilliseconds(300));
		Assert.That(integration.InitializeCount, Is.EqualTo(1));
	}

	[Test]
	public async Task A_host_initiated_goodbye_then_reconnect_opens_a_new_session_and_re_initializes()
	{
		var integration = new TestIntegration("test.a17.b");

		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => integration);

		await using var plugin = await host.HostAsync(builder);
		var first = await host.WaitForSessionAsync();

		await WaitForInitializeCount(integration, 1);

		await host.SendAsync(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionGoodbye,
			Id = Guid.CreateVersion7().ToString(),
			Payload = System.Text.Json.JsonSerializer.SerializeToElement(new SessionGoodbyePayload { Reason = "test" },
				PluginProtocolJson.Options)
		});

		await host.DisconnectAsync((int)WebSocketCloseStatus.NormalClosure);

		var second = await host.WaitForSessionAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
			Assert.That(second.Resumed, Is.False);
		});

		await WaitForInitializeCount(integration, 2);
	}

	[Test]
	public async Task A_second_instance_of_the_same_plugin_id_replaces_the_first_session()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		var credentials = PluginTestCredentials.Managed;

		PluginHostBuilder Build(string integrationId) => MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration(integrationId));

		// Both plugins share credentials (the same PluginTestCredentials.Managed instance), so
		// HostAsync gives both the identical id via configuration regardless of what each build's own
		// default manifest would otherwise have declared - exactly the "same plugin id" this test needs
		// to prove the second instance replaces the first's session.
		await using var firstPlugin = await host.HostAsync(Build("test.a17.c.first"), credentials);
		var first = await host.WaitForSessionAsync();

		await using var secondPlugin = await host.HostAsync(Build("test.a17.c.second"), credentials);
		var second = await host.WaitForSessionAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
			Assert.That(host.DeletedSessions, Does.Contain(first.SessionId));

			// The replaced session's socket must actually close with SessionReplaced, not merely be
			// forgotten by the host's own bookkeeping - see MacroDeckTestHost.CloseReplacedConnectionAsync.
			Assert.That(host.Messages.Closes,
				Has.Some.Matches<RecordedProtocolClose>(close =>
					close.CloseCode == ProtocolCloseCodes.SessionReplaced));
		});
	}

	private static async Task WaitForInitializeCount(TestIntegration integration, int expected)
		=> await Wait.UntilAsync(() => integration.InitializeCount == expected,
			TimeSpan.FromSeconds(10),
			because: $"InitializeCount never reached {expected} (was {integration.InitializeCount})");
}

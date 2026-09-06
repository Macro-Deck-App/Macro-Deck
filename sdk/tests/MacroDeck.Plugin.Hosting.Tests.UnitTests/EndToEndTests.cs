using System.Net;
using MacroDeck.Plugin.Hosting.Endpoints;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Actions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// The whole handshake against a host that really speaks the protocol over a real socket. Everything
/// else is tested against seams; these prove the seams were the right shape.
/// </summary>
[TestFixture]
public class EndToEndTests
{
	private FakePluginHost _host = null!;
	private string _stateDirectory = string.Empty;
	private PluginManifestFixture _manifest = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await FakePluginHost.StartAsync();
		_stateDirectory = Directory.CreateTempSubdirectory("macro-deck-plugin-e2e").FullName;
		_manifest = new PluginManifestFixture("""
											  {
											    "manifestVersion": 1,
											    "id": "com.example.test",
											    "name": "Test Plugin",
											    "version": "1.0.0"
											  }
											  """);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
		Directory.Delete(_stateDirectory, recursive: true);
		_manifest.Dispose();
	}

	private PluginHostBuilder Builder(bool managed = false, params IActionDefinition[] actions)
	{
		var builder = _manifest.CreateBuilder()
			.RegisterIntegration(_ => new TestIntegration(actions));

		builder.Configuration["MacroDeck:Plugin:HostUrl"] = _host.Url;
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = _stateDirectory;

		if (managed)
		{
			builder.Configuration["MacroDeck:Plugin:Id"] = "com.example.test";
			builder.Configuration["MacroDeck:Plugin:Secret"] = new string('s', 43);
		}
		else
		{
			builder.Configuration["MacroDeck:Plugin:EnrollmentToken"] = "enrollment-token";
		}

		return builder;
	}

	[Test]
	public async Task A_self_registering_plugin_registers_then_opens_a_session_and_connects()
	{
		await using var plugin = Builder(actions: new TestAction("play")).Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await _host.WelcomedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_host.EnrollmentTokens, Is.EqualTo(new[] { "enrollment-token" }));
			Assert.That(_host.Registrations.Single().PluginId, Is.EqualTo("com.example.test"));
			Assert.That(_host.Registrations.Single().DisplayName, Is.EqualTo("Test Plugin"));
			Assert.That(_host.Sessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task The_declared_capabilities_reach_the_session_request()
	{
		await using var plugin = Builder(actions: [new TestAction("play"), new TestAction("pause")]).Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(_host.Sessions.Single().Capabilities.Select(capability => capability.LocalId),
			Is.EquivalentTo(new[] { "play", "pause" }));
	}

	[Test]
	public async Task The_upgrade_carries_the_session_token_and_the_subprotocol_but_never_the_secret()
	{
		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(_host.UpgradeAuthorization, Is.EqualTo("Bearer session-token"));
			Assert.That(_host.OfferedSubProtocols, Does.Contain(ProtocolConstants.WebSocketSubProtocol));

			// The secret bought the session token; sending it again would expose it on every reconnect.
			Assert.That(_host.SecretSentOnUpgrade, Is.False);
		});
	}

	[Test]
	public async Task A_managed_plugin_never_registers()
	{
		await using var plugin = Builder(managed: true).Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(_host.Registrations, Is.Empty);
			Assert.That(_host.Sessions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_self_registering_plugin_reuses_its_stored_secret_on_the_next_start()
	{
		await using (var first = Builder().Build())
		{
			await first.StartAsync();
			await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
			await first.StopAsync();
		}

		await using var second = Builder().Build();
		await second.StartAsync();

		// The stored secret is used, so the second start consumes no enrollment token: an author who
		// restarts their plugin should not have to go and get another one.
		await Wait.UntilAsync(() => _host.Sessions.Count == 2);

		Assert.That(_host.Registrations, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task An_action_invocation_round_trips_over_a_real_socket()
	{
		var action = new TestAction("play", _ => Task.FromResult(ActionResult.Success()));

		await using var plugin = Builder(actions: action).Build();
		await plugin.StartAsync();

		var socket = await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await _host.WelcomedAsync();

		await _host.SendAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityInvoke,
				Id = "invoke-1",
				Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
				{
					Kind = CapabilityKinds.Actions,
					LocalId = "play",
					Operation = "execute"
				})
			});

		var result = await _host.NextAsync(MessageTypes.CapabilityResult);

		Assert.Multiple(() =>
		{
			Assert.That(result.CorrelationId, Is.EqualTo("invoke-1"));
			Assert.That(result.Error, Is.Null);
			Assert.That(action.LastContext, Is.Not.Null);
		});
	}

	[Test]
	public async Task Shutdown_says_goodbye_and_ends_the_session()
	{
		var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await _host.WelcomedAsync();

		await plugin.StopAsync();
		await plugin.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_host.Types, Does.Contain(MessageTypes.SessionGoodbye));
			Assert.That(_host.DeletedSessions, Is.EqualTo(new[] { "session-1" }));
		});
	}

	[Test]
	public async Task The_plugin_reports_itself_ready_once_the_session_is_open()
	{
		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		using var client = new HttpClient
		{
			BaseAddress = new Uri(plugin.WebApplication.Services.GetRequiredService<IServer>()
				.Features.Get<IServerAddressesFeature>()!
				.Addresses.First())
		};

		using var ready = await client.GetAsync(new Uri(ReservedPaths.Ready, UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(state.SessionId, Is.EqualTo("session-1"));
			Assert.That(state.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
		});
	}
}

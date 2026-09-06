using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[NonParallelizable]
public class HostStatePushTests
{
	private IHost _host = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private RecordingPluginConnection _connection = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		var registry = _host.Services.GetRequiredService<IPluginSessionRegistry>();
		await registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = "com.example.state",
			DisplayName = "State Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = ProtocolVersions.Current,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = DateTimeOffset.UtcNow
		});

		_connection = new RecordingPluginConnection();
		Assert.That(registry.TryAttach("session-1", _connection, instanceId: null), Is.True);
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[SetUp]
	public void SetUp() => _connection.Clear();

	private static IEnumerable<TestCaseData> StateChanges()
	{
		yield return new TestCaseData(new WidgetDeletedNotification(Guid.NewGuid(), Guid.NewGuid()), HostApis.Widgets)
			.SetName("A widget change pushes the widgets state");
		yield return new TestCaseData(new ScriptDeletedNotification(Guid.NewGuid()), HostApis.Scripts)
			.SetName("A script change pushes the scripts state");
		yield return new TestCaseData(new ProfileDeletedNotification(Guid.NewGuid()), HostApis.Deck)
			.SetName("A deck change pushes the deck state");
	}

	[TestCaseSource(nameof(StateChanges))]
	public async Task A_state_change_pushes_host_state_to_a_connected_plugin(INotification notification, string api)
	{
		await _host.Services.GetRequiredService<IPublisher>().Publish(notification);

		Assert.That(PushedApis(), Does.Contain(api));
	}

	[Test]
	public async Task A_change_of_one_api_does_not_push_the_others()
	{
		await _host.Services.GetRequiredService<IPublisher>()
			.Publish(new ScriptDeletedNotification(Guid.NewGuid()));

		Assert.That(PushedApis(), Is.EqualTo(new[] { HostApis.Scripts }));
	}

	private List<string> PushedApis()
		=> _connection.Sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value
				.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!.Api)
			.ToList();

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class RecordingPluginConnection : IPluginConnection
	{
		private readonly ConcurrentQueue<ProtocolEnvelope> _sent = new();

		public string ConnectionId => "test-connection";

		public IReadOnlyList<ProtocolEnvelope> Sent => _sent.ToList();

		public void Clear() => _sent.Clear();

		public Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
		{
			_sent.Enqueue(envelope);
			return Task.CompletedTask;
		}

		public Task Close(int closeCode, string reason, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}

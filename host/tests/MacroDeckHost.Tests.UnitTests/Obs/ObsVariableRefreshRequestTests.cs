using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Obs;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Tests.UnitTests.HomeAssistant;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsVariableRefreshRequestTests
{
	[Test]
	public async Task An_obs_websocket_state_change_asks_for_an_eager_refresh()
	{
		var fake = new FakeObsClient { IsConnected = true, Status = new ObsStatus { CurrentScene = "Live" } };
		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal);
		signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId);

		fake.RaiseStateChanged();

		Assert.That(signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId), Is.True);
	}

	[Test]
	public async Task Connecting_asks_for_an_eager_refresh()
	{
		var fake = new FakeObsClient { Status = new ObsStatus { CurrentScene = "Intro" } };
		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal);
		signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId);

		fake.IsConnected = true;
		fake.RaiseConnected();

		Assert.That(signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId), Is.True);
	}

	[Test]
	public async Task The_status_poll_timer_does_not_ask_for_an_eager_refresh()
	{
		var calls = 0;
		var fake = new FakeObsClient { IsConnected = true, Status = new ObsStatus { CurrentScene = "Live" } };
		fake.QueryStatusHandler = () =>
		{
			Interlocked.Increment(ref calls);
			return new ObsStatus { CurrentScene = "Live" };
		};

		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal, TimeSpan.FromMilliseconds(20));
		fake.RaiseConnected();

		await WaitForQueryStatusCalls(() => calls, Volatile.Read(ref calls) + 3);
		signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId);

		await WaitForQueryStatusCalls(() => calls, Volatile.Read(ref calls) + 5);

		Assert.That(signal.DrainEagerRefreshRequested(ObsIntegration.IntegrationId),
			Is.False,
			"the status timer would lift variables that change continuously, such as cpu_usage, above their "
			+ "declared refresh interval");
	}

	private static async Task WaitForQueryStatusCalls(Func<int> calls, int target)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (calls() < target)
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail($"the status timer never reached {target} reads");
			}

			await Task.Delay(10);
		}
	}

	private static async Task<ObsIntegration> StartIntegration(
		FakeObsClient client,
		IVariableRefreshSignal signal,
		TimeSpan? pollInterval = null)
	{
		var integration = new ObsIntegration(_ => client, pollInterval);
		integration.UseVariableRefreshSignal(signal);
		await integration.InitializeAsync(new ObsContext());
		return integration;
	}

	private sealed class ObsContext : IIntegrationContext
	{
		public IIntegrationConfig Config { get; } = new SingleEntryConfig();
		public IVariableApi Variables { get; } = new NoOpVariableApi();
		public IEventPublisher Events { get; } = new NoOpEventPublisher();
		public IUserVariableApi UserVariables => throw new NotSupportedException();
		public IDeckNavigator Deck => throw new NotSupportedException();
		public IScriptApi Scripts => throw new NotSupportedException();
		public IWidgetApi Widgets => throw new NotSupportedException();
		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class SingleEntryConfig : IIntegrationConfig
	{
		private static readonly Guid _entryId = Guid.NewGuid();

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(
			CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(
				[new ConfigEntrySnapshot(_entryId, "mdfi")]);

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(key switch
			{
				ObsConfigurationMetadata.SchemaKey => ObsConfigurationMetadata.SchemaVersion,
				ObsConfigurationMetadata.VariableIdentityKey =>
					ObsConfigurationMetadata.SerializeIdentity(new ObsConfigurationIdentity("mdfi", "mdfi")),
				"host" => "127.0.0.1",
				"port" => "4455",
				_ => null
			});

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(null);

		public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken ct = default)
			=> Task.CompletedTask;

		public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken ct = default)
			=> Task.CompletedTask;
	}
}

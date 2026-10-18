using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Obs;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Obs.Configuration;
using MacroDeckHost.Integrations.Obs.Protocol;
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
		var fake = new FakeObsClient
		{
			IsConnected = true, Status = new ObsStatus { CurrentScene = "Live" },
			QueryStatusHandler = () =>
			{
				Interlocked.Increment(ref calls);
				return new ObsStatus { CurrentScene = "Live" };
			}
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

	[Test]
	public async Task A_mute_change_in_obs_asks_for_the_muted_variable_of_that_input()
	{
		var fake = new FakeObsClient { IsConnected = true };
		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal);

		fake.RaiseInputMuteChanged("Mic", true);

		var requested = await WaitForDefinitionRequests(signal, 1);
		Assert.That(WithoutRuntimeId(requested), Is.EqualTo(new[] { "/input/Mic/muted" }));
	}

	[TestCase(ObsInputAudioProperty.Volume, new[] { "/input/Mic/volume" })]
	[TestCase(ObsInputAudioProperty.MonitorType, new[] { "/input/Mic/monitor-type" })]
	[TestCase(ObsInputAudioProperty.SyncOffset, new[] { "/input/Mic/sync-offset" })]
	[TestCase(ObsInputAudioProperty.Tracks,
		new[]
		{
			"/input/Mic/track-1", "/input/Mic/track-2", "/input/Mic/track-3", "/input/Mic/track-4",
			"/input/Mic/track-5", "/input/Mic/track-6"
		})]
	public async Task An_audio_change_in_obs_asks_for_the_matching_variables_of_that_input(
		ObsInputAudioProperty property,
		string[] expectedSuffixes)
	{
		var fake = new FakeObsClient { IsConnected = true };
		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal);

		fake.RaiseInputAudioChanged("Mic", property);

		var requested = await WaitForDefinitionRequests(signal, expectedSuffixes.Length);
		Assert.That(WithoutRuntimeId(requested), Is.EquivalentTo(expectedSuffixes));
	}

	[Test]
	public async Task A_scene_item_shown_or_hidden_in_obs_asks_for_the_visibility_variable_of_that_source()
	{
		var fake = new FakeObsClient { IsConnected = true };
		fake.SceneItemSources[("Live", 7)] = "Camera";
		var signal = new VariableRefreshSignal();
		using var integration = await StartIntegration(fake, signal);

		fake.RaiseSceneItemEnableChanged("Live", 7);

		var requested = await WaitForDefinitionRequests(signal, 1);
		Assert.That(WithoutRuntimeId(requested), Is.EqualTo(new[] { "/scene/Live/item/Camera/visible" }));
	}

	private static IEnumerable<string> WithoutRuntimeId(IEnumerable<string> definitionIds)
	{
		return definitionIds.Select(id => id[Guid.Empty.ToString("D").Length..]);
	}

	private static async Task<List<string>> WaitForDefinitionRequests(VariableRefreshSignal signal, int count)
	{
		var requested = new List<string>();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (requested.Count < count)
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail($"expected {count} definition refresh request(s), got {requested.Count}");
			}

			requested.AddRange(signal.DrainDefinitionsFor(ObsIntegration.IntegrationId, VariableRefreshLane.Eager));
			await Task.Delay(10);
		}

		return requested;
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
		await integration.InitializeAsync(new ObsContext(
			new IntegrationVariableApi(ObsIntegration.IntegrationId, null!, signal)));
		return integration;
	}

	private sealed class ObsContext(IVariableApi variables) : IIntegrationContext
	{
		public IIntegrationConfig Config { get; } = new SingleEntryConfig();
		public IVariableApi Variables { get; } = variables;
		public IEventPublisher Events { get; } = new NoOpEventPublisher();
		public IUserVariableApi UserVariables => null!;
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
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([new ConfigEntrySnapshot(_entryId, "mdfi")]);
		}

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(key switch
			{
				ObsConfigurationMetadata.SchemaKey => ObsConfigurationMetadata.SchemaVersion,
				ObsConfigurationMetadata.VariableIdentityKey =>
					ConfigurationIdentityJson.Serialize(new ConfigurationIdentity("mdfi", "mdfi")),
				"host" => "127.0.0.1",
				"port" => "4455",
				_ => null
			});
		}

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken ct = default)
		{
			return Task.CompletedTask;
		}

		public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken ct = default)
		{
			return Task.CompletedTask;
		}
	}
}

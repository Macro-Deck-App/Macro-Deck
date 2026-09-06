using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsMultipleConfigurationsTests
{
	private static readonly string[] _actionIds =
	[
		"set-scene", "set-preview-scene", "start-recording", "stop-recording", "toggle-recording",
		"toggle-pause-recording", "start-streaming", "stop-streaming", "toggle-streaming",
		"start-virtual-camera", "stop-virtual-camera", "toggle-virtual-camera", "start-replay-buffer",
		"stop-replay-buffer", "toggle-replay-buffer", "save-replay-buffer", "set-source-visibility",
		"set-input-mute", "set-input-volume", "set-source-filter", "get-input-volume",
		"get-source-filter-state", "get-input-mute", "get-source-visibility", "toggle-studio-mode"
	];

	private static readonly string[] _eventIds =
	[
		"scene-changed", "preview-scene-changed", "recording-started", "recording-stopped",
		"recording-paused", "recording-resumed", "streaming-started", "streaming-stopped",
		"replay-buffer-started", "replay-buffer-stopped", "replay-buffer-saved", "virtual-cam-started",
		"virtual-cam-stopped", "studio-mode-changed", "input-mute-changed", "connected", "disconnected"
	];

	private static readonly string[] _bScene = ["B scene"];

	[Test]
	public void Catalog_requires_stable_configuration_identity_without_changing_ids()
	{
		using var integration = new ObsIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.AllowsMultipleConfigurations, Is.True);
			Assert.That(integration.Actions.Select(action => action.Id), Is.EquivalentTo(_actionIds));
			Assert.That(integration.EventDefinitions.Select(definition => definition.Id), Is.EquivalentTo(_eventIds));
		});

		foreach (var action in integration.Actions)
		{
			var configuration = action.Parameters.Single(parameter =>
				parameter.Name == ObsTargetResolver.ConfigurationParameter);
			Assert.Multiple(() =>
			{
				Assert.That(configuration.Required, Is.True, action.Id);
				Assert.That(configuration.DynamicOptions, Is.True, action.Id);
				Assert.That(configuration.DefaultValue, Is.Null, action.Id);
			});
		}

		foreach (var definition in integration.EventDefinitions)
		{
			var configuration = definition.ConfigurationParameters.Single(parameter =>
				parameter.Name == ObsTargetResolver.ConfigurationParameter);
			var payload = definition.PayloadParameters.Single(parameter =>
				parameter.Name == ObsTargetResolver.ConfigurationParameter);
			Assert.Multiple(() =>
			{
				Assert.That(configuration.Required, Is.True, definition.Id);
				Assert.That(configuration.DynamicOptions, Is.True, definition.Id);
				Assert.That(configuration.DefaultValue, Is.Null, definition.Id);
				Assert.That(payload.Required, Is.True, definition.Id);
				Assert.That(definition.ConfigurationParameters
						.Where(parameter => parameter.Name != ObsTargetResolver.ConfigurationParameter)
						.All(parameter => !parameter.Required),
					Is.True,
					definition.Id);
			});
		}
	}

	[Test]
	public async Task Actions_and_options_only_use_the_selected_runtime()
	{
		var aId = Guid.NewGuid();
		var bId = Guid.NewGuid();
		var aClient = new FakeObsClient { IsConnected = false, SceneNames = ["A scene"] };
		var bClient = new FakeObsClient { IsConnected = true, SceneNames = ["B scene"] };
		await using var aConnection = new ObsConnection(aClient, "ws://a:4455", null);
		await using var bConnection = new ObsConnection(bClient, "ws://b:4455", null);
		var runtimes = new List<ObsRuntime>
		{
			Runtime(aId, "A", "a", aConnection),
			Runtime(bId, "B", "b", bConnection)
		};
		var actions = ObsActions.Create(new ObsTargetResolver(() => runtimes), new VariableApiAccessor());
		var toggle = actions.Single(action => action.Id == "toggle-recording");

		var bResult = await toggle.CreateExecutor().ExecuteAsync(Context(bId));
		var aResult = await toggle.CreateExecutor().ExecuteAsync(Context(aId));
		var missingResult = await toggle.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{ Parameters = new Dictionary<string, object>() });
		var unknownResult = await toggle.CreateExecutor().ExecuteAsync(Context(Guid.NewGuid()));
		var sceneAction = (IDynamicOptionsActionDefinition)actions.Single(action => action.Id == "set-scene");
		var bOptions = await sceneAction.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = SceneActionDefinition.SceneParameter,
				CurrentParameters = new Dictionary<string, object?>
					{ [ObsTargetResolver.ConfigurationParameter] = bId.ToString("D") }
			},
			CancellationToken.None);
		var aOptions = await sceneAction.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = SceneActionDefinition.SceneParameter,
				CurrentParameters = new Dictionary<string, object?>
					{ [ObsTargetResolver.ConfigurationParameter] = aId.ToString("D") }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(bResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(bClient.Calls, Does.Contain("ToggleRecord"));
			Assert.That(aClient.Calls, Does.Not.Contain("ToggleRecord"));
			Assert.That(aResult.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(missingResult.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(unknownResult.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(bOptions.Options.Select(option => option.Value), Is.EqualTo(_bScene));
			Assert.That(aOptions.Options, Is.Empty);
		});
	}

	[Test]
	public void Variable_identities_are_collision_safe_and_definitions_survive_rename()
	{
		var main = ObsConfigurationIdentityAllocator.Allocate("Main OBS",
			new HashSet<string>(),
			suffixFactory: () => 1234)!;
		var collision = ObsConfigurationIdentityAllocator.Allocate("main_obs",
			new HashSet<string> { main.Key },
			suffixFactory: () => 5678)!;
		var empty = ObsConfigurationIdentityAllocator.Allocate("📺", new HashSet<string>(), suffixFactory: () => 2468)!;
		var renamed = ObsConfigurationIdentityAllocator.Allocate("Home!",
			new HashSet<string>(),
			main.Key,
			suffixFactory: () => 1357)!;
		var entryId = Guid.NewGuid();
		var before = ObsVariables.Declare(main.Key, entryId);
		var after = ObsVariables.Declare(renamed.Key, entryId);

		Assert.Multiple(() =>
		{
			Assert.That(collision.Key, Is.Not.EqualTo(main.Key));
			Assert.That(collision.Key, Does.StartWith("main_obs_"));
			Assert.That(empty.Key, Does.StartWith("configuration_"));
			Assert.That(renamed.Key, Is.Not.EqualTo(main.Key));
			Assert.That(before, Has.Count.EqualTo(20));
			Assert.That(before.Select(variable => variable.Name), Is.Unique);
			Assert.That(after.Select(variable => variable.Id),
				Is.EqualTo(before.Select(variable => variable.Id)));
			Assert.That(after.Select(variable => variable.Name),
				Is.Not.EqualTo(before.Select(variable => variable.Name)));
			Assert.That(before.All(variable => variable.Id is { Length: < 64 }), Is.True);
		});
	}

	[Test]
	public async Task Delete_is_rejected_without_explicit_confirmation()
	{
		var coordinator = new IntegrationConfigMutationCoordinator(null!, null!, null!, null!, null!, [], null!);

		var result = await coordinator.DeleteAsync(ObsIntegration.IntegrationId,
			Guid.NewGuid(),
			confirmed: false,
			CancellationToken.None);

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public async Task Renaming_a_legacy_entry_does_not_migrate_it_without_reconfiguration()
	{
		var harness = new MutationHarness();
		var entryId = Guid.NewGuid();
		harness.Store.Entries.Add(new ConfigEntryRecord(entryId,
			ObsIntegration.IntegrationId,
			"Legacy",
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[ObsConfigKeys.Host] = JsonSerializer.SerializeToElement("localhost"),
				[ObsConfigKeys.Port] = JsonSerializer.SerializeToElement("4455")
			}));

		var outcome = await harness.Coordinator.RenameAsync(ObsIntegration.IntegrationId,
			entryId,
			"Renamed legacy",
			CancellationToken.None);
		var stored = await harness.Store.Find(entryId);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(stored!.Title, Is.EqualTo("Renamed legacy"));
			Assert.That(stored.Values, Does.Not.ContainKey(ObsConfigurationMetadata.SchemaKey));
			Assert.That(stored.Values, Does.Not.ContainKey(ObsConfigurationMetadata.VariableIdentityKey));
			Assert.That(harness.Adapter.IsUsable(stored), Is.False);
			Assert.That(harness.VariableRegistry.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task Renaming_a_configuration_keeps_its_stored_name_in_sync()
	{
		var harness = new MutationHarness();
		var entryId = harness.AddEntry("Streaming PC", "streaming_pc");

		var outcome = await harness.Coordinator.RenameAsync(ObsIntegration.IntegrationId,
			entryId,
			"Recording PC",
			CancellationToken.None);
		var stored = await harness.Store.Find(entryId);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(stored!.Title, Is.EqualTo("Recording PC"));
			Assert.That(stored.Values[ObsConfigKeys.ConfigurationName].GetString(), Is.EqualTo("Recording PC"));
		});
	}

	[Test]
	public async Task Each_configurations_variables_carry_that_configurations_title()
	{
		var harness = new MutationHarness();
		var a = harness.AddEntry("A", "a");
		var b = harness.AddEntry("B", "b");

		await harness.Adapter.SynchronizeVariablesAsync(harness.Store.Entries, CancellationToken.None);

		var variables = await harness.Variables.GetByOwnerIntegration(ObsIntegration.IntegrationId);
		var aVariables = variables.Where(variable =>
			ObsVariables.TryGetConfigurationEntryId(variable.DefinitionId, out var entryId) && entryId == a).ToList();
		var bVariables = variables.Where(variable =>
			ObsVariables.TryGetConfigurationEntryId(variable.DefinitionId, out var entryId) && entryId == b).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(aVariables, Is.Not.Empty);
			Assert.That(bVariables, Is.Not.Empty);

			foreach (var variable in aVariables)
			{
				Assert.That(variable.Presentation?.ConfigurationKey, Is.EqualTo(a.ToString("N")), variable.Name);
				Assert.That(variable.Presentation?.ConfigurationName.Literal, Is.EqualTo("A"), variable.Name);
			}

			foreach (var variable in bVariables)
			{
				Assert.That(variable.Presentation?.ConfigurationKey, Is.EqualTo(b.ToString("N")), variable.Name);
				Assert.That(variable.Presentation?.ConfigurationName.Literal, Is.EqualTo("B"), variable.Name);
			}
		});
	}

	[Test]
	public async Task Confirmed_delete_removes_only_exact_configuration_variables_and_preserves_push_variables()
	{
		var harness = new MutationHarness();
		var a = harness.AddEntry("A", "a");
		var b = harness.AddEntry("B", "b");
		await harness.Adapter.SynchronizeVariablesAsync(harness.Store.Entries, CancellationToken.None);
		var push = await harness.Variables.CreateIntegrationVariable(ObsIntegration.IntegrationId,
			"obs_push_status",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"push-status");

		var first = await harness.Coordinator.DeleteAsync(ObsIntegration.IntegrationId,
			a,
			confirmed: true,
			CancellationToken.None);
		var afterFirst = await harness.Variables.GetByOwnerIntegration(ObsIntegration.IntegrationId);
		var second = await harness.Coordinator.DeleteAsync(ObsIntegration.IntegrationId,
			b,
			confirmed: true,
			CancellationToken.None);
		var afterSecond = await harness.Variables.GetByOwnerIntegration(ObsIntegration.IntegrationId);

		Assert.Multiple(() =>
		{
			Assert.That(first.Success, Is.True);
			Assert.That(afterFirst.Count(variable =>
					ObsVariables.TryGetConfigurationEntryId(variable.DefinitionId, out _)),
				Is.EqualTo(20));
			Assert.That(afterFirst.Any(variable => variable.Id == push.Data!.Id), Is.True);
			Assert.That(second.Success, Is.True);
			Assert.That(afterSecond.Select(variable => variable.Id), Is.EqualTo(new[] { push.Data!.Id }));
			Assert.That(harness.Registry.IsEnabled(ObsIntegration.IntegrationId), Is.False);
		});
	}

	[Test]
	public async Task Registry_wide_collision_allocates_a_numeric_key_before_persisting_all_variables()
	{
		var harness = new MutationHarness(() => 1234);
		await harness.Variables.CreateUserVariable("obs_studio_current_scene",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);
		var entryId = Guid.NewGuid();

		var outcome = await harness.Coordinator.CompleteAsync(ObsIntegration.IntegrationId,
			entryId,
			"Studio",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);
		var stored = await harness.Store.Find(entryId);
		var identityJson = stored!.Values[ObsConfigurationMetadata.VariableIdentityKey].GetString();
		var parsed = ObsConfigurationMetadata.TryParseIdentity(identityJson, out var identity);
		var variables = await harness.Variables.GetByOwnerIntegration(ObsIntegration.IntegrationId);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(parsed, Is.True);
			Assert.That(stored.Values[ObsConfigKeys.ConfigurationName].GetString(), Is.EqualTo("Studio"));
			Assert.That(identity.Key, Is.EqualTo("studio_1234"));
			Assert.That(variables, Has.Count.EqualTo(20));
			Assert.That(
				variables.All(variable => variable.Name.StartsWith("obs_studio_1234_", StringComparison.Ordinal)),
				Is.True);
		});
	}

	[Test]
	public async Task Registry_wide_collision_exhaustion_is_localized_and_leaves_no_partial_configuration_or_variables()
	{
		var harness = new MutationHarness(() => 1234);
		await harness.Variables.CreateUserVariable("obs_studio_current_scene",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);
		await harness.Variables.CreateUserVariable("obs_studio_1234_current_scene",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);

		var outcome = await harness.Coordinator.CompleteAsync(ObsIntegration.IntegrationId,
			Guid.NewGuid(),
			"Studio",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.False);
			Assert.That(TestLocalization.Resolve(outcome.Error), Is.Not.Null.And.Not.Empty);
			Assert.That(harness.Store.Entries, Is.Empty);
			Assert.That(harness.VariableRegistry.GetAll(), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_late_status_query_cannot_resurrect_a_disposed_configuration_or_affect_another_connection()
	{
		var entered = new ManualResetEventSlim();
		var release = new ManualResetEventSlim();
		var publisher = new RecordingEventPublisher();
		var aClient = new FakeObsClient { IsConnected = true, Status = ConnectedStatus("Before") };
		var bClient = new FakeObsClient { IsConnected = true, Status = ConnectedStatus("B") };
		var aConnection = new ObsConnection(aClient,
			"ws://a:4455",
			null,
			events: new ObsEventEmitter(publisher, Guid.NewGuid()));
		await using var bConnection = new ObsConnection(bClient, "ws://b:4455", null);
		aClient.RaiseStateChanged();
		publisher.Events.Clear();
		aClient.QueryStatusHandler = () =>
		{
			entered.Set();
			release.Wait();
			return ConnectedStatus("After disposal");
		};
		var lateCallback = Task.Run(aClient.RaiseStateChanged);
		Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);

		await aConnection.DisposeAsync();
		var bCommand = await bConnection.ToggleRecordingAsync();
		release.Set();
		await lateCallback.WaitAsync(TimeSpan.FromSeconds(2));

		Assert.Multiple(() =>
		{
			Assert.That(aConnection.State.IsConnected, Is.False);
			Assert.That(publisher.Events, Is.Empty);
			Assert.That(bCommand, Is.True);
			Assert.That(bClient.Calls, Does.Contain("ToggleRecord"));
		});
	}

	private static ObsRuntime Runtime(Guid id, string title, string key, ObsConnection connection)
		=> new(id,
			title,
			new ObsConfigurationIdentity(title, key),
			new ObsConfigurationSettings(title.ToLowerInvariant(), 4455, null),
			connection);

	private static ActionExecutionContext Context(Guid configurationId)
		=> new()
		{
			Parameters = new Dictionary<string, object>
				{ [ObsTargetResolver.ConfigurationParameter] = configurationId.ToString("D") }
		};

	private static ObsStatus ConnectedStatus(string scene)
		=> new() { CurrentScene = scene };

	private sealed class RecordingEventPublisher : IEventPublisher
	{
		public List<string> Events { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Events.Add(eventId);
	}

	private sealed class MutationHarness
	{
		public MutationHarness(Func<int>? suffixFactory = null)
		{
			Registry = new ConfigurableIntegrationRegistry([]);
			VariableRegistry = new VariableRegistry();
			Variables = new VariableService(VariableRegistry,
				new NullUserVariableStore(),
				new RecordingMediator(),
				new VariableCatalogProviders(Registry),
				new VariableRefreshSignal(),
				new MusicPlayerPollNudge(Registry));
			Store = new MemoryConfigStore();
			var services = new ServiceCollection();
			services.AddSingleton<IIntegrationConfigStore>(Store);
			services.AddScoped<IVariableService>(_ => Variables);
			var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
			Adapter = new ObsConfigurationMutationAdapter(Registry, VariableRegistry, scopeFactory, suffixFactory);
			Coordinator = new IntegrationConfigMutationCoordinator(scopeFactory,
				new RecordingLifecycle(),
				Registry,
				new RecordingMediator(),
				new VariablePollingInvalidationSignal(),
				[Adapter],
				Serilog.Log.Logger);
		}

		public ConfigurableIntegrationRegistry Registry { get; }
		public VariableRegistry VariableRegistry { get; }
		public VariableService Variables { get; }
		public MemoryConfigStore Store { get; }
		public ObsConfigurationMutationAdapter Adapter { get; }
		public IntegrationConfigMutationCoordinator Coordinator { get; }

		public Guid AddEntry(string title, string key)
		{
			var id = Guid.NewGuid();
			Store.Entries.Add(ConfigurationRecord(id, title, key));
			return id;
		}
	}

	private static ConfigEntryRecord ConfigurationRecord(Guid id, string title, string key)
		=> new(id,
			ObsIntegration.IntegrationId,
			title,
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[ObsConfigurationMetadata.SchemaKey]
					= JsonSerializer.SerializeToElement(ObsConfigurationMetadata.SchemaVersion),
				[ObsConfigurationMetadata.VariableIdentityKey] = JsonSerializer.SerializeToElement(
					ObsConfigurationMetadata.SerializeIdentity(new ObsConfigurationIdentity(title, key)))
			});

	private sealed class MemoryConfigStore : IIntegrationConfigStore
	{
		public List<ConfigEntryRecord> Entries { get; } = [];

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(Entries
				.Where(entry => entry.IntegrationId == integrationId)
				.Select(entry => new ConfigEntrySummary(entry.Id, entry.IntegrationId, entry.Title, entry.CreatedAt))
				.ToList());

		public Task<ConfigEntryRecord?> Find(Guid entryId)
			=> Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == entryId));

		public Task<Guid> Create(
			string integrationId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			var id = Guid.NewGuid();
			Entries.Add(new ConfigEntryRecord(id, integrationId, title, DateTime.UtcNow, values));
			return Task.FromResult(id);
		}

		public Task<bool> Create(
			Guid entryId,
			string integrationId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			if (Entries.Any(entry => entry.Id == entryId))
			{
				return Task.FromResult(false);
			}

			Entries.Add(new ConfigEntryRecord(entryId, integrationId, title, DateTime.UtcNow, values));
			return Task.FromResult(true);
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> Replace(entryId, Entries.First(entry => entry.Id == entryId).Title, values);

		public Task<bool> Replace(
			Guid entryId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			var index = Entries.FindIndex(entry => entry.Id == entryId);
			if (index < 0)
			{
				return Task.FromResult(false);
			}

			Entries[index] = Entries[index] with { Title = title, Values = values };
			return Task.FromResult(true);
		}

		public Task Delete(Guid entryId)
		{
			Entries.RemoveAll(entry => entry.Id == entryId);
			return Task.CompletedTask;
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	private sealed class RecordingLifecycle : IIntegrationLifecycle
	{
		public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}

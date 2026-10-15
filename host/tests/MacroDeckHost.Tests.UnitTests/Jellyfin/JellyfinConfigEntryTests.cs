using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Configuration;
using MacroDeckHost.Integrations.Jellyfin.State;
using MacroDeckHost.Integrations.Jellyfin.Variables;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using DomainEnums = MacroDeckHost.Domain.Enums;
using static MacroDeckHost.Tests.UnitTests.Jellyfin.JellyfinSessions;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinConfigEntryTests
{
	private MemoryConfigStore _store = null!;
	private VariableRegistry _variables = null!;
	private JellyfinIntegration _integration = null!;
	private IntegrationConfigMutationCoordinator _coordinator = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new MemoryConfigStore();
		_variables = new VariableRegistry();
		_integration = new JellyfinIntegration(_ => new FakeJellyfinClient(),
			timings: FastTimings,
			suffixFactory: () => 1234);

		var services = new ServiceCollection();
		services.AddSingleton<IIntegrationConfigStore>(_store);
		_coordinator = new IntegrationConfigMutationCoordinator(
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new NoLifecycle(),
			new ConfigurableIntegrationRegistry([_integration]),
			new RecordingMediator(),
			new VariablePollingInvalidationSignal(),
			Serilog.Core.Logger.None,
			_variables);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	[Test]
	public async Task Saving_an_edit_again_keeps_devices_learned_while_the_dialog_was_open_and_the_variable_key()
	{
		var entryId = Guid.NewGuid();
		_store.Entries.Add(new ConfigEntryRecord(entryId,
			JellyfinIntegration.IntegrationId,
			"Home",
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[JellyfinIntegration.VariableKeyConfigKey] = JsonSerializer.SerializeToElement("home"),
				[JellyfinConfigKeys.Devices] = JsonSerializer.SerializeToElement("[\"current\"]")
			}));
		foreach (var variable in JellyfinVariables.DeclareServer("home", entryId, "Home"))
		{
			OwnVariable(variable.Name!,
				variable.Id!,
				JellyfinIntegration.IntegrationId,
				SdkVariableTypeMapper.ToDomain(variable.Type));
		}

		var outcome = await _coordinator.CompleteAsync(JellyfinIntegration.IntegrationId,
			entryId,
			"Home",
			new Dictionary<string, JsonElement>
			{
				[JellyfinConfigKeys.Devices] = JsonSerializer.SerializeToElement("[\"stale\"]")
			},
			CancellationToken.None);

		var stored = _store.Entries.Single();
		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(stored.Values[JellyfinConfigKeys.Devices].GetString(), Is.EqualTo("[\"current\"]"));
			Assert.That(stored.Values[JellyfinIntegration.VariableKeyConfigKey].GetString(), Is.EqualTo("home"));
		});
	}

	[Test]
	public async Task A_new_server_whose_variable_name_another_integration_owns_is_refused()
	{
		var taken = JellyfinVariables.ServerVariableName("home", JellyfinVariables.ServerSlotNames[0]);
		OwnVariable(taken, taken, "app.example.other", DomainEnums.VariableType.Text);

		var outcome = await _coordinator.CompleteAsync(JellyfinIntegration.IntegrationId,
			Guid.NewGuid(),
			"Home",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.False);
			Assert.That(outcome.Error.ToString(),
				Is.EqualTo(((LocalizedText)AppStrings.Errors.Config.VariableNamesUnavailable()).ToString()));
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_renamed_server_gets_a_new_variable_key_and_another_servers_key_is_never_reused()
	{
		var entryId = Guid.NewGuid();
		_store.Entries.Add(Entry(Guid.NewGuid(), "Cabin", "cabin"));
		_store.Entries.Add(Entry(entryId, "Home", "home"));

		var outcome = await _coordinator.RenameAsync(JellyfinIntegration.IntegrationId,
			entryId,
			"Cabin",
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(_store.Entries.Single(entry => entry.Id == entryId)
					.Values[JellyfinIntegration.VariableKeyConfigKey]
					.GetString(),
				Is.EqualTo("cabin_1234"));
		});
	}

	[Test]
	public async Task Synchronizing_creates_each_servers_and_devices_variables_and_deletes_those_of_removed_servers()
	{
		var context = new JellyfinTestContext();
		await _integration.InitializeAsync(context);
		var removed = Guid.NewGuid();
		await context.Variables.CreateAsync(JellyfinVariables.DeclareServer("gone", removed, "Gone")[0]);
		var known = new JellyfinDeviceRegistry();
		known.Observe(JellyfinSessionMapper.Map([Session("tv", "Living Room TV")], "x"),
			DateTimeOffset.UtcNow,
			_ => false);
		var kept = Guid.NewGuid();

		await _integration.SynchronizeEntriesAsync([
			Stored(kept, "Home", "home", known.Serialize()),
			Stored(Guid.NewGuid(), "Legacy", null, null)
		]);

		var names = context.OwnVariables.All.Select(variable => variable.Name).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(names,
				Is.SupersetOf(JellyfinVariables.DeclareServer("home", kept, "Home").Select(variable => variable.Name)));
			Assert.That(names, Does.Contain(JellyfinVariables.DeviceVariableName("home", "living_room_tv", "title")));
			Assert.That(names, Has.None.StartsWith("jellyfin_gone_"));
			Assert.That(names, Has.None.Contains("legacy"));
		});
	}

	[Test]
	public async Task An_entry_without_a_variable_key_needs_reconfiguring_and_one_without_a_connection_is_disconnected()
	{
		var legacy = await _integration.GetEntryStatusAsync(Stored(Guid.NewGuid(), "Legacy", null, null));
		var idle = await _integration.GetEntryStatusAsync(Stored(Guid.NewGuid(), "Home", "home", null));

		Assert.Multiple(() =>
		{
			Assert.That(legacy, Is.EqualTo(ConfigEntryStatus.NeedsReconfiguration));
			Assert.That(idle, Is.EqualTo(ConfigEntryStatus.Disconnected));
		});
	}

	private void OwnVariable(string name, string definitionId, string owner, DomainEnums.VariableType type)
	{
		_variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = DomainEnums.VariableScope.Global,
			Type = type,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = owner,
			DefinitionId = definitionId
		});
	}

	private static ConfigEntryRecord Entry(Guid id, string title, string key)
	{
		return new ConfigEntryRecord(id,
			JellyfinIntegration.IntegrationId,
			title,
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[JellyfinIntegration.VariableKeyConfigKey] = JsonSerializer.SerializeToElement(key)
			});
	}

	private static StoredConfigEntry Stored(Guid id, string title, string? key, string? devices)
	{
		var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (key is not null)
		{
			values[JellyfinIntegration.VariableKeyConfigKey] = JsonSerializer.SerializeToElement(key);
		}

		if (devices is not null)
		{
			values[JellyfinConfigKeys.Devices] = JsonSerializer.SerializeToElement(devices);
		}

		return new StoredConfigEntry { Id = id, Title = title, Values = values };
	}

	private sealed class NoLifecycle : IIntegrationLifecycle
	{
		public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class MemoryConfigStore : IIntegrationConfigStore
	{
		public List<ConfigEntryRecord> Entries { get; } = [];

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(Entries
				.Where(entry => entry.IntegrationId == integrationId)
				.Select(entry => new ConfigEntrySummary(entry.Id, entry.IntegrationId, entry.Title, entry.CreatedAt))
				.ToList());
		}

		public Task<ConfigEntryRecord?> Find(Guid entryId)
		{
			return Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == entryId));
		}

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
		{
			throw new NotSupportedException();
		}

		public Task<bool> Create(
			Guid entryId,
			string integrationId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			Entries.Add(new ConfigEntryRecord(entryId, integrationId, title, DateTime.UtcNow, values));
			return Task.FromResult(true);
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
		{
			throw new NotSupportedException();
		}

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
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
}

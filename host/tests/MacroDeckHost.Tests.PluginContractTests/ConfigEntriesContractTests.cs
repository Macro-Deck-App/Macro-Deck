using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class ConfigEntriesContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
	{
		return new DeclaredCapability
		{
			Kind = CapabilityKinds.ConfigEntries,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};
	}

	private static StoredConfigEntry Entry(bool withKey)
	{
		return new StoredConfigEntry
		{
			Id = Guid.NewGuid(),
			Title = "Home",
			Values = withKey
				? new Dictionary<string, JsonElement> { ["key"] = JsonSerializer.SerializeToElement("home") }
				: new Dictionary<string, JsonElement>()
		};
	}

	[Test]
	public async Task Every_member_round_trips_to_the_plugins_lifecycle()
	{
		var plugin = new ServersIntegration();
		var integration = (IConfigEntryLifecycle)await ConnectAsync([new ConfigEntriesCapabilityHandler([plugin])],
			[Provider()],
			[CapabilityKinds.ConfigEntries]);
		var entryId = Guid.NewGuid();

		var prepared = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = entryId, Title = "Home", Values = new Dictionary<string, JsonElement>()
		});
		var refused = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = entryId, Title = " ", Values = new Dictionary<string, JsonElement>()
		});
		var status = await integration.GetEntryStatusAsync(Entry(true));
		var usable = await integration.IsEntryUsableAsync(Entry(false));
		var reloaded = await integration.ReloadEntriesAsync();
		await integration.SynchronizeEntriesAsync([Entry(true)]);
		await integration.ReleasePreparationAsync(entryId);

		Assert.Multiple(() =>
		{
			Assert.That(prepared.Success, Is.True);
			Assert.That(prepared.Values!["key"].GetString(), Is.EqualTo("home"));
			Assert.That(
				prepared.ReservedVariables.Select(variable => (variable.Name, variable.ResolvedId, variable.Type)),
				Is.EqualTo(new[]
				{
					("server_home", VariableDefinition.Eager("server_home", VariableType.Text).ResolvedId,
						VariableType.Text)
				}));
			Assert.That(refused.Success, Is.False);
			Assert.That(refused.Error.Literal, Is.EqualTo("A title is required."));
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Connected));
			Assert.That(usable, Is.False);
			Assert.That(reloaded, Is.True);
			Assert.That(plugin.Synchronized, Is.EqualTo(1));
			Assert.That(plugin.Released, Is.EqualTo(new[] { entryId }));
		});
	}

	[Test]
	public async Task Rejected_names_reach_the_plugin_and_its_retry_request_reaches_the_host()
	{
		var plugin = new ServersIntegration { RetriesOnConflict = true };
		var integration = (IConfigEntryLifecycle)await ConnectAsync([new ConfigEntriesCapabilityHandler([plugin])],
			[Provider()],
			[CapabilityKinds.ConfigEntries]);

		var prepared = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "Home",
			Values = new Dictionary<string, JsonElement>(),
			RejectedVariableNames = ["server_home"]
		});

		Assert.Multiple(() =>
		{
			Assert.That(prepared.RetryOnConflict, Is.True);
			Assert.That(plugin.Rejections.Single(), Is.EqualTo(new[] { "server_home" }));
		});
	}

	[Test]
	public async Task A_plugin_written_before_the_variables_overload_is_synchronized_when_the_host_passes_variables()
	{
		var plugin = new ServersIntegration();
		var integration = (IConfigEntryLifecycle)await ConnectAsync([new ConfigEntriesCapabilityHandler([plugin])],
			[Provider()],
			[CapabilityKinds.ConfigEntries]);

		await integration.SynchronizeEntriesAsync([Entry(true)], new NoVariables(), CancellationToken.None);

		Assert.That(plugin.Synchronized, Is.EqualTo(1));
	}

	[Test]
	public async Task A_plugin_that_did_not_declare_the_capability_gets_the_defaults_without_a_round_trip()
	{
		var integration = (IConfigEntryLifecycle)await ConnectAsync([], [], []);

		var prepared = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(), Title = "Home", Values = new Dictionary<string, JsonElement>()
		});
		var status = await integration.GetEntryStatusAsync(Entry(false));
		var reloaded = await integration.ReloadEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(prepared.Success, Is.True);
			Assert.That(prepared.Values, Is.Null);
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Ready));
			Assert.That(reloaded, Is.False);
		});
	}

	private sealed class NoVariables : IVariableApi
	{
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle?> GetByNameAsync(string name)
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			throw new NotSupportedException();
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			throw new NotSupportedException();
		}

		public Task DeleteAsync(Guid variableId)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class ServersIntegration : IPluginIntegration, IConfigEntryLifecycle
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public int Synchronized { get; private set; }

		public bool RetriesOnConflict { get; init; }

		public List<IReadOnlyList<string>> Rejections { get; } = [];

		public List<Guid> Released { get; } = [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
			ConfigEntryPreparation preparation,
			CancellationToken cancellationToken = default)
		{
			Rejections.Add(preparation.RejectedVariableNames);
			return Task.FromResult(string.IsNullOrWhiteSpace(preparation.Title)
				? ConfigEntryPreparationResult.Refuse("A title is required.")
				: ConfigEntryPreparationResult.Accept(new Dictionary<string, JsonElement>
							{ ["key"] = JsonSerializer.SerializeToElement("home") },
						[VariableDefinition.Eager("server_home", VariableType.Text)]) with
					{
						RetryOnConflict = RetriesOnConflict
					});
		}

		public Task<ConfigEntryStatus> GetEntryStatusAsync(
			StoredConfigEntry entry,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ConfigEntryStatus.Connected);
		}

		public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(entry.Values.ContainsKey("key"));
		}

		public Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(true);
		}

		public Task SynchronizeEntriesAsync(
			IReadOnlyList<StoredConfigEntry> entries,
			CancellationToken cancellationToken = default)
		{
			Synchronized++;
			return Task.CompletedTask;
		}

		public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
		{
			Released.Add(entryId);
			return Task.CompletedTask;
		}
	}
}

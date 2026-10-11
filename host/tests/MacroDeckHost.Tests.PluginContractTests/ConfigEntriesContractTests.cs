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
		=> new()
		{
			Kind = CapabilityKinds.ConfigEntries,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static StoredConfigEntry Entry(bool withKey)
		=> new()
		{
			Id = Guid.NewGuid(),
			Title = "Home",
			Values = withKey
				? new Dictionary<string, JsonElement> { ["key"] = JsonSerializer.SerializeToElement("home") }
				: new Dictionary<string, JsonElement>()
		};

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
		var status = await integration.GetEntryStatusAsync(Entry(withKey: true));
		var usable = await integration.IsEntryUsableAsync(Entry(withKey: false));
		var reloaded = await integration.ReloadEntriesAsync();
		await integration.SynchronizeEntriesAsync([Entry(withKey: true)]);
		await integration.ReleasePreparationAsync(entryId);

		Assert.Multiple(() =>
		{
			Assert.That(prepared.Success, Is.True);
			Assert.That(prepared.Values!["key"].GetString(), Is.EqualTo("home"));
			Assert.That(prepared.ReservedVariables.Select(variable => (variable.Name, variable.ResolvedId, variable.Type)),
				Is.EqualTo(new[] { ("server_home", VariableDefinition.Eager("server_home", VariableType.Text).ResolvedId, VariableType.Text) }));
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
	public async Task A_plugin_that_did_not_declare_the_capability_gets_the_defaults_without_a_round_trip()
	{
		var integration = (IConfigEntryLifecycle)await ConnectAsync([], [], []);

		var prepared = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(), Title = "Home", Values = new Dictionary<string, JsonElement>()
		});
		var status = await integration.GetEntryStatusAsync(Entry(withKey: false));
		var reloaded = await integration.ReloadEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(prepared.Success, Is.True);
			Assert.That(prepared.Values, Is.Null);
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Ready));
			Assert.That(reloaded, Is.False);
		});
	}

	private sealed class ServersIntegration : IPluginIntegration, IConfigEntryLifecycle
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public int Synchronized { get; private set; }

		public List<Guid> Released { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<ConfigEntryPreparationResult> PrepareEntryAsync(ConfigEntryPreparation preparation,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(string.IsNullOrWhiteSpace(preparation.Title)
				? ConfigEntryPreparationResult.Refuse("A title is required.")
				: ConfigEntryPreparationResult.Accept(
					new Dictionary<string, JsonElement> { ["key"] = JsonSerializer.SerializeToElement("home") },
					[VariableDefinition.Eager("server_home", VariableType.Text)]));

		public Task<ConfigEntryStatus> GetEntryStatusAsync(StoredConfigEntry entry,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(ConfigEntryStatus.Connected);

		public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
			=> Task.FromResult(entry.Values.ContainsKey("key"));

		public Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

		public Task SynchronizeEntriesAsync(IReadOnlyList<StoredConfigEntry> entries,
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

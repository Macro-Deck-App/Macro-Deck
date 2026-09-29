using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class OptionalConfigurationRemovalTests
{
	[Test]
	public async Task Removing_the_only_entry_of_an_optional_configuration_keeps_the_integration_running()
	{
		var harness = new Harness(requiresConfiguration: false);
		var entryId = harness.AddEntry();

		var outcome = await harness.Coordinator.DeleteAsync(Harness.IntegrationId, entryId, confirmed: true,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(harness.Registry.IsEnabled(Harness.IntegrationId), Is.True);
			Assert.That(harness.Lifecycle.Shutdowns, Is.Empty);
			Assert.That(harness.Lifecycle.Reinitializations, Is.EqualTo(new[] { Harness.IntegrationId }));
		});
	}

	[Test]
	public async Task Removing_the_only_entry_of_a_required_configuration_stops_the_integration()
	{
		var harness = new Harness(requiresConfiguration: true);
		var entryId = harness.AddEntry();

		var outcome = await harness.Coordinator.DeleteAsync(Harness.IntegrationId, entryId, confirmed: true,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(harness.Registry.IsEnabled(Harness.IntegrationId), Is.False);
			Assert.That(harness.Lifecycle.Shutdowns, Is.EqualTo(new[] { Harness.IntegrationId }));
		});
	}

	private sealed class Harness
	{
		public const string IntegrationId = "test.settings";

		private readonly MemoryConfigStore _store = new();

		public Harness(bool requiresConfiguration)
		{
			Registry = new ConfigurableIntegrationRegistry([
				new FakeConfigurableVariableProviderIntegration
				{
					Id = IntegrationId,
					IsInitialized = true,
					RequiresConfiguration = requiresConfiguration
				}
			]);
			var services = new ServiceCollection();
			services.AddSingleton<IIntegrationConfigStore>(_store);
			Coordinator = new IntegrationConfigMutationCoordinator(
				services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
				Lifecycle,
				Registry,
				new RecordingMediator(),
				new VariablePollingInvalidationSignal(),
				[],
				Serilog.Core.Logger.None);
		}

		public ConfigurableIntegrationRegistry Registry { get; }

		public RecordingLifecycle Lifecycle { get; } = new();

		public IntegrationConfigMutationCoordinator Coordinator { get; }

		public Guid AddEntry()
		{
			var id = Guid.NewGuid();
			_store.Entries.Add(new ConfigEntryRecord(id, IntegrationId, "Settings", DateTime.UtcNow,
				new Dictionary<string, JsonElement> { ["cycling"] = JsonSerializer.SerializeToElement("true") }));
			return id;
		}
	}

	private sealed class RecordingLifecycle : IIntegrationLifecycle
	{
		public List<string> Reinitializations { get; } = [];

		public List<string> Shutdowns { get; } = [];

		public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			Reinitializations.Add(integrationId);
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			Shutdowns.Add(integrationId);
			return Task.CompletedTask;
		}
	}

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

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task Delete(Guid entryId)
		{
			Entries.RemoveAll(entry => entry.Id == entryId);
			return Task.CompletedTask;
		}
	}
}

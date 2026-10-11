using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using DomainEnums = MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class ConfigEntryLifecycleTests
{
	private const string IntegrationId = "app.example.servers";

	private MemoryConfigStore _store = null!;
	private VariableRegistry _variables = null!;
	private RecordingLifecycle _lifecycle = null!;
	private ServersIntegration _integration = null!;
	private IntegrationConfigMutationCoordinator _coordinator = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new MemoryConfigStore();
		_variables = new VariableRegistry();
		_lifecycle = new RecordingLifecycle();
		_integration = new ServersIntegration();

		var services = new ServiceCollection();
		services.AddSingleton<IIntegrationConfigStore>(_store);
		_coordinator = new IntegrationConfigMutationCoordinator(
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			_lifecycle,
			new ConfigurableIntegrationRegistry([_integration]),
			new RecordingMediator(),
			new VariablePollingInvalidationSignal(),
			[],
			Serilog.Core.Logger.None,
			_variables);
	}

	private Task<IntegrationConfigMutationOutcome> CompleteAsync(Guid entryId, string title)
		=> _coordinator.CompleteAsync(IntegrationId,
			entryId,
			title,
			new Dictionary<string, JsonElement> { ["url"] = JsonSerializer.SerializeToElement("http://nas") },
			CancellationToken.None);

	private void AddVariable(string name, string owner, string definitionId)
		=> _variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Text,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = owner,
			DefinitionId = definitionId
		});

	[Test]
	public async Task A_completed_entry_is_stored_as_the_integration_prepared_it_and_synchronized()
	{
		var entryId = Guid.NewGuid();

		var outcome = await CompleteAsync(entryId, "Home");

		var stored = _store.Entries.Single();
		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(stored.Values["key"].GetString(), Is.EqualTo("home"));
			Assert.That(stored.Values["url"].GetString(), Is.EqualTo("http://nas"));
			Assert.That(_integration.Synchronized.Single().Select(entry => entry.Id), Is.EqualTo(new[] { entryId }));
			Assert.That(_integration.Released, Is.EqualTo(new[] { entryId }));
		});
	}

	[Test]
	public async Task An_entry_whose_variable_name_another_integration_owns_is_refused()
	{
		AddVariable("server_home_status", "app.example.other", "server_home_status");

		var outcome = await CompleteAsync(Guid.NewGuid(), "Home");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.False);
			Assert.That(outcome.Error.ToString(),
				Is.EqualTo(((LocalizedText)AppStrings.Errors.Config.VariableNamesUnavailable()).ToString()));
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task Saving_an_entry_again_whose_variable_the_integration_already_owns_is_no_conflict()
	{
		var entryId = Guid.NewGuid();
		await CompleteAsync(entryId, "Home");
		AddVariable("server_home_status", IntegrationId, VariableDefinitionId.FromName("server_home_status")!);

		var outcome = await CompleteAsync(entryId, "Home");

		Assert.That(outcome.Success, Is.True);
	}

	[Test]
	public async Task A_refusal_from_the_integration_reaches_the_user_and_stores_nothing()
	{
		var outcome = await CompleteAsync(Guid.NewGuid(), " ");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.False);
			Assert.That(outcome.Error.Literal, Is.EqualTo("A title is required."));
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task The_integration_reports_each_entrys_status_and_usability()
	{
		await CompleteAsync(Guid.NewGuid(), "Home");
		_store.Entries.Add(new ConfigEntryRecord(Guid.NewGuid(), IntegrationId, "Legacy", DateTime.UtcNow,
			new Dictionary<string, JsonElement>()));

		var described = await _coordinator.DescribeAsync(IntegrationId, CancellationToken.None);

		Assert.That(described.Select(entry => (entry.Title, entry.Status, entry.Usable)),
			Is.EquivalentTo(new[]
			{
				("Home", IntegrationConfigEntryStatus.Connected, true),
				("Legacy", IntegrationConfigEntryStatus.NeedsReconfiguration, false)
			}));
	}

	[Test]
	public async Task A_slow_integration_is_asked_about_every_entry_at_once_and_the_order_is_kept()
	{
		foreach (var title in new[] { "Alpha", "Beta", "Gamma" })
		{
			_store.Entries.Add(new ConfigEntryRecord(Guid.NewGuid(), IntegrationId, title, DateTime.UtcNow,
				new Dictionary<string, JsonElement>()));
		}

		_integration.Stall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var describing = _coordinator.DescribeAsync(IntegrationId, CancellationToken.None);
		await CalendarTree.WaitForAsync(() => _integration.Waiting == 6,
			"the status and usability of every entry were not requested together");
		_integration.Stall.SetResult();
		var described = await describing;

		Assert.That(described.Select(entry => entry.Title), Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));
	}

	[Test]
	public async Task An_integration_that_applies_the_change_itself_is_not_initialized_again()
	{
		_integration.AppliesChanges = true;

		await CompleteAsync(Guid.NewGuid(), "Home");

		Assert.Multiple(() =>
		{
			Assert.That(_integration.Reloads, Is.EqualTo(1));
			Assert.That(_lifecycle.Reinitializations, Is.Empty);
		});
	}

	[Test]
	public async Task An_integration_that_does_not_apply_the_change_itself_is_initialized_again()
	{
		_integration.AppliesChanges = false;

		await CompleteAsync(Guid.NewGuid(), "Home");

		Assert.That(_lifecycle.Reinitializations, Is.EqualTo(new[] { IntegrationId }));
	}

	private sealed class ServersIntegration : IIntegration, IConfigEntryLifecycle
	{
		public string Id => IntegrationId;

		public LocalizedText Name => "Servers";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public bool AppliesChanges { get; set; } = true;

		public int Reloads { get; private set; }

		public List<IReadOnlyList<StoredConfigEntry>> Synchronized { get; } = [];

		public List<Guid> Released { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<ConfigEntryPreparationResult> PrepareEntryAsync(ConfigEntryPreparation preparation,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(preparation.Title))
			{
				return Task.FromResult(ConfigEntryPreparationResult.Refuse("A title is required."));
			}

			var key = preparation.Title.Trim().ToLowerInvariant();
			var values = new Dictionary<string, JsonElement>(preparation.Values)
			{
				["key"] = JsonSerializer.SerializeToElement(key)
			};

			return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
				[VariableDefinition.Eager($"server_{key}_status", VariableType.Text)]));
		}

		public TaskCompletionSource? Stall { get; set; }

		public int Waiting => Volatile.Read(ref _waiting);

		private int _waiting;

		public async Task<ConfigEntryStatus> GetEntryStatusAsync(StoredConfigEntry entry,
			CancellationToken cancellationToken = default)
		{
			await StallAsync();
			return entry.Values.ContainsKey("key")
				? ConfigEntryStatus.Connected
				: ConfigEntryStatus.NeedsReconfiguration;
		}

		public async Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
		{
			await StallAsync();
			return entry.Values.ContainsKey("key");
		}

		private async Task StallAsync()
		{
			if (Stall is { } stall)
			{
				Interlocked.Increment(ref _waiting);
				await stall.Task;
			}
		}

		public Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
		{
			Reloads++;
			return Task.FromResult(AppliesChanges);
		}

		public Task SynchronizeEntriesAsync(IReadOnlyList<StoredConfigEntry> entries,
			CancellationToken cancellationToken = default)
		{
			Synchronized.Add(entries);
			return Task.CompletedTask;
		}

		public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
		{
			Released.Add(entryId);
			return Task.CompletedTask;
		}
	}

	private sealed class RecordingLifecycle : IIntegrationLifecycle
	{
		public List<string> Reinitializations { get; } = [];

		public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			Reinitializations.Add(integrationId);
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
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

		public Task<bool> Create(Guid entryId,
			string integrationId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			Entries.Add(new ConfigEntryRecord(entryId, integrationId, title, DateTime.UtcNow, values));
			return Task.FromResult(true);
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

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

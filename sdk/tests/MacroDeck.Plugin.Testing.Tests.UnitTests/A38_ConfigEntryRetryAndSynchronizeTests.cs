using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A38ConfigEntryRetryAndSynchronizeTests
{
	private static readonly string[] _homeStatus = ["server_home_status"];

	[Test]
	public async Task A_repeated_prepare_carries_the_rejected_names_and_answers_the_retry_request()
	{
		var integration = new ServersIntegration();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => integration));

		var outcome = await harness.ConfigEntries.PrepareAsync(new ConfigEntryPrepareArguments
		{
			EntryId = Guid.NewGuid(),
			Title = "Home",
			Values = new Dictionary<string, JsonElement>(),
			RejectedVariableNames = ["server_home_status"]
		});

		Assert.Multiple(() =>
		{
			Assert.That(outcome.DataAs<ConfigEntryPrepareResult>()!.RetryOnConflict, Is.True);
			Assert.That(outcome.DataAs<ConfigEntryPrepareResult>()!.ReservedVariables.Single().Name,
				Is.EqualTo("server_home_2_status"));
		});
	}

	[Test]
	public async Task Synchronizing_an_integration_that_was_never_initialized_creates_its_variables_in_the_fake()
	{
		var integration = new ServersIntegration();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => integration));

		await harness.ConfigEntries.SynchronizeAsync(new ConfigEntrySynchronizeArguments
		{
			Entries = [new ConfigEntryDto { Id = Guid.NewGuid(), Title = "Home" }]
		});

		Assert.That(harness.Context.Variables.Created.Select(variable => variable.Name),
			Is.EqualTo(_homeStatus));
	}

	private sealed class ServersIntegration : IPluginIntegration, IConfigEntryLifecycle
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

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
			var name = preparation.RejectedVariableNames.Contains("server_home_status")
				? "server_home_2_status"
				: "server_home_status";
			return Task.FromResult(ConfigEntryPreparationResult.Accept(null,
					[VariableDefinition.Eager(name, VariableType.Text)]) with
				{
					RetryOnConflict = true
				});
		}

		public async Task SynchronizeEntriesAsync(
			IReadOnlyList<StoredConfigEntry> entries,
			IVariableApi variables,
			CancellationToken cancellationToken)
		{
			foreach (var entry in entries)
			{
				await variables.CreateAsync(VariableDefinition.Eager($"server_{entry.Title.ToLowerInvariant()}_status",
					VariableType.Text));
			}
		}
	}
}

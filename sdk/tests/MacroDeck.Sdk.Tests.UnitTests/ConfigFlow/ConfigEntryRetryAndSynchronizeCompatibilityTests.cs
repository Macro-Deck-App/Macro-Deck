using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.ConfigFlow;

[TestFixture]
public class ConfigEntryRetryAndSynchronizeCompatibilityTests
{
	[Test]
	public async Task An_implementer_written_before_the_variables_overload_is_synchronized_through_its_own_method()
	{
		var lifecycle = new SynchronizesWithoutVariables();
		IConfigEntryLifecycle asInterface = lifecycle;
		IReadOnlyList<StoredConfigEntry> entries =
		[
			new()
				{ Id = Guid.NewGuid(), Title = "Home" }
		];

		await asInterface.SynchronizeEntriesAsync(entries, new NoVariables(), CancellationToken.None);

		Assert.That(lifecycle.Synchronized, Is.EqualTo(new[] { entries }));
	}

	[Test]
	public async Task A_call_written_before_the_variables_overload_still_compiles_and_reaches_the_same_method()
	{
		var lifecycle = new SynchronizesWithoutVariables();
		IConfigEntryLifecycle asInterface = lifecycle;
		IReadOnlyList<StoredConfigEntry> entries = [];

		await asInterface.SynchronizeEntriesAsync(entries, CancellationToken.None);
		await asInterface.SynchronizeEntriesAsync(entries);

		Assert.That(lifecycle.Synchronized, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task An_implementer_written_before_retries_sees_a_first_attempt_and_is_never_retried()
	{
		IConfigEntryLifecycle lifecycle = new SynchronizesWithoutVariables();
		var preparation = new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(), Title = "Home", Values = new Dictionary<string, System.Text.Json.JsonElement>()
		};

		var result = await lifecycle.PrepareEntryAsync(preparation);

		Assert.Multiple(() =>
		{
			Assert.That(preparation.RejectedVariableNames, Is.Empty);
			Assert.That(result.RetryOnConflict, Is.False);
			Assert.That(ConfigEntryPreparationResult.Accept().RetryOnConflict, Is.False);
			Assert.That(ConfigEntryPreparationResult.Refuse("No").RetryOnConflict, Is.False);
		});
	}

	[Test]
	public void Completing_without_an_entry_is_a_completion_that_stores_nothing()
	{
		var withoutEntry = ConfigFlowResult.CompleteWithoutEntry();
		var complete = ConfigFlowResult.Complete("Home");

		Assert.Multiple(() =>
		{
			Assert.That(withoutEntry.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(withoutEntry.StoresEntry, Is.False);
			Assert.That(withoutEntry.EntryTitle, Is.Null);
			Assert.That(withoutEntry.Values, Is.Null);
			Assert.That(complete.StoresEntry, Is.True);
		});
	}

	private sealed class SynchronizesWithoutVariables : IConfigEntryLifecycle
	{
		public List<IReadOnlyList<StoredConfigEntry>> Synchronized { get; } = [];

		public Task SynchronizeEntriesAsync(
			IReadOnlyList<StoredConfigEntry> entries,
			CancellationToken cancellationToken = default)
		{
			Synchronized.Add(entries);
			return Task.CompletedTask;
		}
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
}

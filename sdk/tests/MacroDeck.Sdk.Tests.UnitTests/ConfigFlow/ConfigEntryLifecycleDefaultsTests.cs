using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Sdk.Tests.UnitTests.ConfigFlow;

[TestFixture]
public class ConfigEntryLifecycleDefaultsTests
{
	[Test]
	public async Task Every_default_matches_an_integration_without_the_interface()
	{
		IConfigEntryLifecycle lifecycle = new NothingOverridden();
		var entry = new StoredConfigEntry { Id = Guid.NewGuid(), Title = "Home" };

		var prepared = await lifecycle.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = entry.Id, Title = "Home", Values = entry.Values
		});

		var usable = await lifecycle.IsEntryUsableAsync(entry);
		var status = await lifecycle.GetEntryStatusAsync(entry);
		var reloaded = await lifecycle.ReloadEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(prepared.Success, Is.True);
			Assert.That(prepared.Values, Is.Null);
			Assert.That(prepared.ReservedVariables, Is.Empty);
			Assert.That(usable, Is.True);
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Ready));
			Assert.That(reloaded, Is.False);
		});
	}

	private sealed class NothingOverridden : IConfigEntryLifecycle;
}

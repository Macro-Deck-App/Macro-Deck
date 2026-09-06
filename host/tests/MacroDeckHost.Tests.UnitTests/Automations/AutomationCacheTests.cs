using MacroDeckHost.Application.Persistence.Automations;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Automations;

[TestFixture]
public class AutomationCacheTests
{
	private static AutomationEntity Automation(string name = "Automation", bool enabled = true) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Enabled = enabled,
		Flows = """[{"triggerId":"t1","triggerType":"onEvent","children":[]}]""",
		CreatedAt = DateTime.UtcNow,
		UpdatedAt = DateTime.UtcNow
	};

	private static AutomationCache Cache(InMemoryAutomationStore store)
		=> new(store, new LoggerConfiguration().CreateLogger());

	[Test]
	public async Task InitializeCache_loads_every_stored_automation()
	{
		var store = new InMemoryAutomationStore(
			new AutomationFile { Id = Guid.NewGuid(), Name = "First", Enabled = true },
			new AutomationFile { Id = Guid.NewGuid(), Name = "Second", Enabled = false });
		using var cache = Cache(store);

		await cache.InitializeCache();

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetAll(), Has.Count.EqualTo(2));
			Assert.That(cache.GetAll().Single(a => a.Name == "Second").Enabled, Is.False);
		});
	}

	[Test]
	public async Task A_file_without_the_enabled_flag_loads_as_enabled()
	{
		var store = new InMemoryAutomationStore(new AutomationFile { Id = Guid.NewGuid(), Name = "Legacy" });
		using var cache = Cache(store);

		await cache.InitializeCache();

		Assert.That(cache.GetAll().Single().Enabled, Is.True);
	}

	[Test]
	public async Task AddOrUpdate_writes_through_to_the_store()
	{
		var store = new InMemoryAutomationStore();
		using var cache = Cache(store);
		await cache.InitializeCache();
		var automation = Automation();

		await cache.AddOrUpdate(automation);

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetById(automation.Id), Is.SameAs(automation));
			Assert.That(store.Get(automation.Id)!.Flows, Is.EqualTo(automation.Flows));
			Assert.That(store.SaveCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task AddOrUpdate_replaces_an_existing_automation()
	{
		var store = new InMemoryAutomationStore();
		using var cache = Cache(store);
		await cache.InitializeCache();
		var automation = Automation();
		await cache.AddOrUpdate(automation);

		automation.Enabled = false;
		await cache.AddOrUpdate(automation);

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetAll(), Has.Count.EqualTo(1));
			Assert.That(store.Get(automation.Id)!.Enabled, Is.False);
		});
	}

	[Test]
	public async Task Remove_deletes_the_file()
	{
		var store = new InMemoryAutomationStore();
		using var cache = Cache(store);
		await cache.InitializeCache();
		var automation = Automation();
		await cache.AddOrUpdate(automation);

		await cache.Remove(automation.Id);

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetById(automation.Id), Is.Null);
			Assert.That(store.Get(automation.Id), Is.Null);
			Assert.That(store.DeleteCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Remove_of_an_unknown_id_does_not_touch_the_store()
	{
		var store = new InMemoryAutomationStore();
		using var cache = Cache(store);
		await cache.InitializeCache();

		await cache.Remove(Guid.NewGuid());

		Assert.That(store.DeleteCount, Is.Zero);
	}

	[Test]
	public async Task InitializeCache_is_idempotent()
	{
		var store = new InMemoryAutomationStore(new AutomationFile { Id = Guid.NewGuid(), Name = "First" });
		using var cache = Cache(store);

		await cache.InitializeCache();
		await cache.InitializeCache();

		Assert.That(cache.GetAll(), Has.Count.EqualTo(1));
	}
}

using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginRuntimeStateStoreTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void A_saved_state_round_trips_through_a_fresh_store()
	{
		var store = new PluginRuntimeStateStore(_paths, Log.Logger);

		store.Save("com.example.plugin", true).GetAwaiter().GetResult();

		var reloaded = new PluginRuntimeStateStore(_paths, Log.Logger).Load();
		Assert.That(reloaded["com.example.plugin"], Is.True);
	}

	[Test]
	public void Loading_before_any_save_returns_an_empty_map_without_throwing()
	{
		var store = new PluginRuntimeStateStore(_paths, Log.Logger);

		Assert.That(store.Load(), Is.Empty);
	}

	[Test]
	public void Saving_one_plugin_does_not_disturb_another()
	{
		var store = new PluginRuntimeStateStore(_paths, Log.Logger);

		store.Save("com.a", true).GetAwaiter().GetResult();
		store.Save("com.b", false).GetAwaiter().GetResult();
		store.Save("com.a", false).GetAwaiter().GetResult();

		var state = store.Load();
		Assert.Multiple(() =>
		{
			Assert.That(state["com.a"], Is.False);
			Assert.That(state["com.b"], Is.False);
		});
	}

	[Test]
	public void Removing_a_plugin_drops_its_entry_and_leaves_the_others()
	{
		var store = new PluginRuntimeStateStore(_paths, Log.Logger);
		store.Save("com.a", true).GetAwaiter().GetResult();
		store.Save("com.b", true).GetAwaiter().GetResult();

		store.Remove("com.a").GetAwaiter().GetResult();

		var state = new PluginRuntimeStateStore(_paths, Log.Logger).Load();
		Assert.Multiple(() =>
		{
			Assert.That(state.ContainsKey("com.a"), Is.False);
			Assert.That(state["com.b"], Is.True);
		});
	}

	[Test]
	public void Removing_an_unknown_plugin_changes_nothing()
	{
		var store = new PluginRuntimeStateStore(_paths, Log.Logger);
		store.Save("com.a", true).GetAwaiter().GetResult();

		store.Remove("com.unknown").GetAwaiter().GetResult();

		Assert.That(new PluginRuntimeStateStore(_paths, Log.Logger).Load()["com.a"], Is.True);
	}
}

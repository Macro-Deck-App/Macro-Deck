using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
internal sealed class JsonIntegrationStateStoreTests
{
	private TestPaths _paths = null!;
	private string _path = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_path = Path.Combine(_paths.DataDirectory, "integration-states.json");
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void Saved_states_round_trip_through_a_fresh_store()
	{
		new JsonIntegrationStateStore(_paths, Log.Logger).Save(new Dictionary<string, bool> { ["obs"] = false });

		Assert.That(new JsonIntegrationStateStore(_paths, Log.Logger).Load()["obs"], Is.False);
	}

	[Test]
	public void Saves_after_an_unreadable_load_keep_every_unreadable_copy()
	{
		Directory.CreateDirectory(_paths.DataDirectory);
		string[] originals = ["{ \"obs\": tru", "{ \"obs\": fals", "{ \"spotify\": tr"];
		File.WriteAllText(_path, originals[0]);
		File.WriteAllText(_path + ".bak", originals[1]);
		File.WriteAllText(_path + ".tmp", originals[2]);
		var store = new JsonIntegrationStateStore(_paths, Log.Logger);

		Assert.That(store.Load(), Is.Empty);
		store.Save(new Dictionary<string, bool> { ["obs"] = true });
		store.Save(new Dictionary<string, bool> { ["obs"] = false });

		var surviving = Directory
			.GetFiles(_paths.DataDirectory, Path.GetFileName(_path) + "*")
			.Select(File.ReadAllText);
		Assert.That(surviving, Is.SupersetOf(originals));
	}
}

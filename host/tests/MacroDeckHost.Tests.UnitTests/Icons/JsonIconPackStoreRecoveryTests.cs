using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class JsonIconPackStoreRecoveryTests
{
	private TestPaths _paths = null!;
	private JsonIconPackStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new JsonIconPackStore(_paths, new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void LoadAll_RecoversTheManifest_WhenPackJsonIsMissingButATemporaryFileIsValid()
	{
		var packId = Guid.NewGuid();
		var packDirectory = Path.Combine(_paths.IconPacksDirectory, packId.ToString());
		Directory.CreateDirectory(packDirectory);
		var manifestPath = Path.Combine(packDirectory, "pack.json");
		File.WriteAllText(manifestPath + ".tmp", $$"""{"id":"{{packId}}","name":"Recovered"}""");

		var loaded = _store.LoadAll();

		Assert.That(loaded, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(loaded[0].Id, Is.EqualTo(packId));
			Assert.That(File.Exists(manifestPath), Is.True);
			Assert.That(File.Exists(manifestPath + ".tmp"), Is.False);
		});
	}
}

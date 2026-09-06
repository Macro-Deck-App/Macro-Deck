using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class JsonIconPackStoreTests
{
	private static readonly int[] _expectedSizes = [128, 256];

	private TestPaths _paths = null!;
	private JsonIconPackStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new JsonIconPackStore(_paths, new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void SaveAndLoadAll_RoundTripsManifest()
	{
		var manifest = new IconPackManifest
		{
			Id = Guid.NewGuid(),
			Name = "My Pack",
			Author = "Tester",
			Version = "1.2.3",
			IsReadOnly = true,
			SourceType = IconPackSourceType.ExtensionStore,
			CreatedAt = DateTime.UtcNow,
			Icons =
			[
				new IconManifestEntry
				{
					Id = Guid.NewGuid(),
					Name = "icon-a",
					State = IconProcessingState.Ready,
					AvailableSizes = [128, 256],
					IsAnimated = true
				}
			]
		};

		_store.Save(manifest);
		var loaded = _store.LoadAll();

		Assert.That(loaded, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(loaded[0].Name, Is.EqualTo("My Pack"));
			Assert.That(loaded[0].IsReadOnly, Is.True);
			Assert.That(loaded[0].SourceType, Is.EqualTo(IconPackSourceType.ExtensionStore));
			Assert.That(loaded[0].Icons, Has.Count.EqualTo(1));
			Assert.That(loaded[0].Icons[0].AvailableSizes, Is.EqualTo(_expectedSizes));
			Assert.That(loaded[0].Icons[0].IsAnimated, Is.True);
		});
	}

	[Test]
	public void Save_WritesCamelCaseManifest()
	{
		var manifest = new IconPackManifest
		{
			Id = Guid.NewGuid(),
			Name = "My Pack",
			IsReadOnly = true,
			SourceType = IconPackSourceType.ExtensionStore
		};

		_store.Save(manifest);

		var json = File.ReadAllText(Path.Combine(_paths.IconPacksDirectory, manifest.Id.ToString(), "pack.json"));
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"name\""));
			Assert.That(json, Does.Contain("\"isReadOnly\""));
			Assert.That(json, Does.Contain("\"sourceType\""));
			Assert.That(json, Does.Not.Contain("\"Name\""));
			Assert.That(json, Does.Not.Contain("\"IsReadOnly\""));
			Assert.That(json, Does.Not.Contain("\"SourceType\""));

			Assert.That(json, Does.Contain("\"ExtensionStore\""));
		});
	}

	[Test]
	public void LoadAll_SkipsCorruptManifest()
	{
		_store.Save(new IconPackManifest { Id = Guid.NewGuid(), Name = "Good" });

		var corruptDirectory = Path.Combine(_paths.IconPacksDirectory, Guid.NewGuid().ToString());
		Directory.CreateDirectory(corruptDirectory);
		File.WriteAllText(Path.Combine(corruptDirectory, "pack.json"), "{ not json !");

		var loaded = _store.LoadAll();

		Assert.That(loaded, Has.Count.EqualTo(1));
		Assert.That(loaded[0].Name, Is.EqualTo("Good"));
	}

	[Test]
	public void Delete_RemovesWholePackFolderIncludingIconFiles()
	{
		var manifest = new IconPackManifest { Id = Guid.NewGuid(), Name = "Doomed" };
		_store.Save(manifest);

		var iconFile = Path.Combine(_paths.IconPacksDirectory, manifest.Id.ToString(), "icons", "x", "master.webp");
		Directory.CreateDirectory(Path.GetDirectoryName(iconFile)!);
		File.WriteAllBytes(iconFile, [1, 2, 3]);

		_store.Delete(manifest.Id);

		Assert.That(Directory.Exists(Path.Combine(_paths.IconPacksDirectory, manifest.Id.ToString())), Is.False);
	}
}

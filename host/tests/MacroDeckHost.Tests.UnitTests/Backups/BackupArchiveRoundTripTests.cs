using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupArchiveRoundTripTests
{
	private TestPaths _paths = null!;
	private byte[] _recoveryKey = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_recoveryKey = RandomNumberGenerator.GetBytes(32);

		Seed("data/profiles/living-room.json", """{"name":"Living Room"}""");
		Seed("data/scripts/greet.json", """{"name":"Greet"}""");
		Seed("data/icons/packs/pack.json", """{"id":"pack"}""");
		Seed("keys/key-11111111.xml", "<key/>");
		Seed("keys/kek.escrow", """{"version":1}""");
		Seed("keys/auth-signing.key", "signing");
		Seed("keys/public-tls.key", "tls");
		Seed("plugins/demo/current.json", """{"version":"1.0.0"}""");

		Seed("logs/host-20260818.log", "noise");
		Seed("plugins/_cache/artifact.bin", "cache");
		Seed("plugins/assets/icon.webp", "asset");
		Seed("data/icons/staging/in-flight.webp", "staging");
		Seed("data/profiles/living-room.json.tmp", """{"name":"unpublished"}""");

		CreateDatabase();
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task RestoresEveryArchivedFileAndLeavesDerivedDataOut()
	{
		var entries = await WriteAndReadInnerEntries();

		Assert.Multiple(() =>
		{
			Assert.That(entries, Contains.Item("files/data/profiles/living-room.json"));
			Assert.That(entries, Contains.Item("files/keys/key-11111111.xml"));
			Assert.That(entries, Contains.Item("files/plugins/demo/current.json"));
			Assert.That(entries, Contains.Item(BackupFileNames.DatabaseEntry));

			Assert.That(entries, Has.None.Contains("logs/"), "logs are rebuildable and must not be archived");
			Assert.That(entries, Has.None.Contains("plugins/_cache/"));
			Assert.That(entries, Has.None.Contains("plugins/assets/"));
			Assert.That(entries, Has.None.Contains("icons/staging/"));
			Assert.That(entries, Has.None.Contains(".tmp"), "an unpublished write must not travel to another host");
		});
	}

	[Test]
	public async Task AssignsEveryArchivedFileToTheGroupThatOwnsIt()
	{
		var index = await WriteAndReadContentIndex();

		string[] expected =
		[
			"data/profiles/living-room.json",
			"data/scripts/greet.json",
			"data/icons/packs/pack.json",
			"keys/key-11111111.xml",
			"keys/kek.escrow",
			"keys/auth-signing.key",
			"keys/public-tls.key",
			"plugins/demo/current.json"
		];

		Assert.Multiple(() =>
		{
			Assert.That(index.Entries.Select(entry => entry.Path), Is.EquivalentTo(expected));
			Assert.That(Component(index, "data/profiles/living-room.json"), Is.EqualTo(BackupComponentGroup.Profiles));
			Assert.That(Component(index, "data/scripts/greet.json"), Is.EqualTo(BackupComponentGroup.Scripts));
			Assert.That(Component(index, "data/icons/packs/pack.json"), Is.EqualTo(BackupComponentGroup.Icons));
			Assert.That(Component(index, "keys/key-11111111.xml"), Is.EqualTo(BackupComponentGroup.Integrations));

			// The escrow is what makes a wrapped ring openable on another machine, so it has to travel
			// with the ring. A file no group owns is dropped from the archive without a word.
			Assert.That(Component(index, "keys/kek.escrow"), Is.EqualTo(BackupComponentGroup.Integrations));
			Assert.That(Component(index, "keys/auth-signing.key"), Is.EqualTo(BackupComponentGroup.Accounts));
			Assert.That(Component(index, "keys/public-tls.key"), Is.EqualTo(BackupComponentGroup.AppSettings));
			Assert.That(Component(index, "plugins/demo/current.json"), Is.EqualTo(BackupComponentGroup.Plugins));
		});
	}

	[Test]
	public async Task RestoresFileContentByteForByte()
	{
		using var archive = new MemoryStream(await WriteArchive());
		var reader = new BackupArchiveReader();
		var payloadPath = Path.Combine(_paths.BaseDirectory, "payload.zip");
		await reader.DecryptPayload(archive, _recoveryKey, payloadPath);

		using var inner = ZipFile.OpenRead(payloadPath);
		var entry = inner.GetEntry("files/data/profiles/living-room.json");

		Assert.That(entry, Is.Not.Null);

		using var stream = entry!.Open();
		using var text = new StreamReader(stream, Encoding.UTF8);

		Assert.That(await text.ReadToEndAsync(), Is.EqualTo("""{"name":"Living Room"}"""));
	}

	[Test]
	public async Task ReadsTheManifestWithoutAnyKeyMaterial()
	{
		using var archive = new MemoryStream(await WriteArchive());

		var manifest = new BackupArchiveReader().ReadManifest(archive);

		Assert.Multiple(() =>
		{
			Assert.That(manifest, Is.Not.Null);
			Assert.That(manifest!.FormatVersion, Is.EqualTo(BackupArchiveManifest.CurrentFormatVersion));
			Assert.That(manifest.Trigger, Is.EqualTo(BackupTrigger.Manual));
		});
	}

	private static BackupComponentGroup Component(BackupContentIndex index, string path)
		=> index.Entries.Single(entry => entry.Path == path).Component;

	private async Task<List<string>> WriteAndReadInnerEntries()
	{
		using var archive = new MemoryStream(await WriteArchive());
		var payloadPath = Path.Combine(_paths.BaseDirectory, "payload.zip");
		await new BackupArchiveReader().DecryptPayload(archive, _recoveryKey, payloadPath);

		using var inner = ZipFile.OpenRead(payloadPath);

		return [.. inner.Entries.Select(entry => entry.FullName)];
	}

	private async Task<BackupContentIndex> WriteAndReadContentIndex()
	{
		using var archive = new MemoryStream(await WriteArchive());
		var payloadPath = Path.Combine(_paths.BaseDirectory, "payload.zip");
		await new BackupArchiveReader().DecryptPayload(archive, _recoveryKey, payloadPath);

		using var inner = ZipFile.OpenRead(payloadPath);
		using var stream = inner.GetEntry(BackupFileNames.ContentIndexEntry)!.Open();

		return JsonSerializer.Deserialize<BackupContentIndex>(stream, PublicJson())!;
	}

	private async Task<byte[]> WriteArchive()
	{
		var source = new BackupSnapshotSource(_paths);
		var plan = source.Plan();
		var databaseCopy = Path.Combine(_paths.BaseDirectory, "snapshot.db");
		await source.CopyDatabase(databaseCopy);

		var manifest = new BackupArchiveManifest
		{
			BackupId = Guid.NewGuid(),
			MacroDeckVersion = "3.0.0",
			CreatedAt = DateTimeOffset.UnixEpoch,
			Trigger = BackupTrigger.Manual,
			RecoveryKeyId = "test",
			HostPlatform = "test",
			Components = [.. BackupComponentGroups.AllIds]
		};

		using var destination = new MemoryStream();
		await new BackupArchiveWriter().Write(destination,
			new BackupArchiveWriteRequest(manifest, plan, databaseCopy),
			_recoveryKey);

		return destination.ToArray();
	}

	private void CreateDatabase()
	{
		using var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString());
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText = "CREATE TABLE app_preference (ap_key TEXT PRIMARY KEY, ap_value TEXT);";
		command.ExecuteNonQuery();
	}

	private void Seed(string relativePath, string content)
	{
		var path = Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	// The archive writes enums as names, which is what lets an older reader keep understanding a manifest
	// after a new value is added.
	private static JsonSerializerOptions PublicJson()
		=> new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

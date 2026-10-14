using MacroDeck.Sdk.Storage;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationStorageTests
{
	private const string IntegrationId = "com.example.storage";
	private const string LegacyKey = "system-audio-devices.json";

	private TestPaths _paths = null!;
	private IntegrationStorageProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.DataDirectory);
		_provider = new IntegrationStorageProvider(_paths, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_provider.Dispose();
		_paths.Cleanup();
	}

	private string Folder => Path.Combine(_paths.DataDirectory, "integration-data", IntegrationId);

	private IIntegrationStorage Storage => _provider.For(IntegrationId);

	private static bool IsDocument(string content)
	{
		return content.StartsWith("doc:", StringComparison.Ordinal);
	}

	[Test]
	public async Task A_key_never_written_reads_as_missing()
	{
		Assert.That((await Storage.ReadAsync("devices.json")).Status, Is.EqualTo(IntegrationStorageReadStatus.Missing));
	}

	[Test]
	public async Task A_written_document_is_read_back_by_a_new_host_instance()
	{
		await Storage.WriteAsync("devices.json", "doc:first");

		using var restarted = new IntegrationStorageProvider(_paths, Serilog.Core.Logger.None);
		var read = await restarted.For(IntegrationId).ReadAsync("devices.json", IsDocument);

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(IntegrationStorageReadStatus.Found));
			Assert.That(read.Content, Is.EqualTo("doc:first"));
			Assert.That(File.ReadAllText(Path.Combine(Folder, "devices.json")), Is.EqualTo("doc:first"));
		});
	}

	[Test]
	public async Task A_damaged_document_falls_back_to_the_copy_kept_by_the_previous_write()
	{
		await Storage.WriteAsync("devices.json", "doc:first");
		await Storage.WriteAsync("devices.json", "doc:second");
		await File.WriteAllTextAsync(Path.Combine(Folder, "devices.json"), "{ torn");

		var read = await Storage.ReadAsync("devices.json", IsDocument);

		Assert.That(read.Content, Is.EqualTo("doc:first"));
	}

	[Test]
	public async Task A_pending_write_is_used_when_the_document_itself_is_missing()
	{
		Directory.CreateDirectory(Folder);
		await File.WriteAllTextAsync(Path.Combine(Folder, "devices.json.tmp"), "doc:pending");

		var read = await Storage.ReadAsync("devices.json", IsDocument);

		Assert.That(read.Content, Is.EqualTo("doc:pending"));
	}

	[Test]
	public async Task Copies_that_exist_but_none_of_which_is_valid_read_as_unreadable_and_stay_untouched()
	{
		Directory.CreateDirectory(Folder);
		await File.WriteAllTextAsync(Path.Combine(Folder, "devices.json"), "{ torn");
		await File.WriteAllTextAsync(Path.Combine(Folder, "devices.json.bak"), string.Empty);

		var read = await Storage.ReadAsync("devices.json", IsDocument);

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(IntegrationStorageReadStatus.Unreadable));
			Assert.That(read.Content, Is.Null);
			Assert.That(File.ReadAllText(Path.Combine(Folder, "devices.json")), Is.EqualTo("{ torn"));
		});
	}

	[Test]
	public async Task A_delete_removes_the_document_and_its_copies()
	{
		await Storage.WriteAsync("devices.json", "doc:first");
		await Storage.WriteAsync("devices.json", "doc:second");

		await Storage.DeleteAsync("devices.json");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Storage.ReadAsync("devices.json")).Status,
				Is.EqualTo(IntegrationStorageReadStatus.Missing));
			Assert.That(Directory.EnumerateFiles(Folder), Is.Empty);
		});
	}

	[TestCase("")]
	[TestCase("Devices.json")]
	[TestCase(".hidden")]
	[TestCase("../escape")]
	[TestCase("nested/key")]
	[TestCase("devices.json.tmp")]
	[TestCase("devices.json.bak")]
	public void An_invalid_key_is_refused_for_every_operation(string key)
	{
		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(() => Storage.ReadAsync(key));
			Assert.ThrowsAsync<ArgumentException>(() => Storage.WriteAsync(key, "doc:x"));
			Assert.ThrowsAsync<ArgumentException>(() => Storage.DeleteAsync(key));
		});
	}

	[Test]
	public async Task Storage_is_private_to_each_integration()
	{
		await Storage.WriteAsync("devices.json", "doc:mine");

		var other = await _provider.For("com.example.other").ReadAsync("devices.json");

		Assert.That(other.Status, Is.EqualTo(IntegrationStorageReadStatus.Missing));
	}

	[Test]
	public async Task A_file_an_earlier_version_kept_in_the_data_directory_is_adopted_with_its_copies()
	{
		var legacy = Path.Combine(_paths.DataDirectory, LegacyKey);
		await File.WriteAllTextAsync(legacy, "{ torn");
		await File.WriteAllTextAsync(legacy + ".bak", "doc:legacy");

		var read = await Storage.ReadAsync(LegacyKey, IsDocument);

		Assert.Multiple(() =>
		{
			Assert.That(read.Content, Is.EqualTo("doc:legacy"));
			Assert.That(File.Exists(legacy), Is.False);
			Assert.That(File.Exists(legacy + ".bak"), Is.False);
			Assert.That(File.Exists(Path.Combine(Folder, LegacyKey + ".bak")), Is.True);
		});
	}

	[Test]
	public async Task A_restored_file_from_before_storage_wins_over_what_storage_already_holds()
	{
		await Storage.WriteAsync(LegacyKey, "doc:current");
		await Storage.WriteAsync(LegacyKey, "doc:newer");
		await File.WriteAllTextAsync(Path.Combine(_paths.DataDirectory, LegacyKey), "doc:restored");

		var read = await Storage.ReadAsync(LegacyKey, IsDocument);
		await File.WriteAllTextAsync(Path.Combine(Folder, LegacyKey), "{ torn");
		var fallback = await Storage.ReadAsync(LegacyKey, IsDocument);

		Assert.Multiple(() =>
		{
			Assert.That(read.Content, Is.EqualTo("doc:restored"));
			Assert.That(fallback.Status,
				Is.EqualTo(IntegrationStorageReadStatus.Unreadable),
				"the copies of the replaced document must not outlive the restore");
		});
	}

	[Test]
	public async Task Only_listed_keys_adopt_a_file_from_the_data_directory()
	{
		await File.WriteAllTextAsync(Path.Combine(_paths.DataDirectory, "devices.json"), "doc:unrelated");

		var read = await Storage.ReadAsync("devices.json");

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(IntegrationStorageReadStatus.Missing));
			Assert.That(File.Exists(Path.Combine(_paths.DataDirectory, "devices.json")), Is.True);
		});
	}

	[Test]
	public void Integration_storage_travels_with_the_integrations_in_a_backup()
	{
		Assert.That(BackupComponentGroups.Owner($"data/integration-data/{IntegrationId}/devices.json"),
			Is.EqualTo(BackupComponentGroup.Integrations));
	}
}

using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Secrets;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class UnreadableStoreFileTests
{
	private const string Damaged = "{ this is not json";

	private TestPaths _paths = null!;
	private ILogger _logger = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_logger = Log.Logger;
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public async Task Changing_one_plugin_keeps_an_unreadable_runtime_state_file_intact()
	{
		var path = Damage(Path.Combine(_paths.ConfigDirectory, "plugin-runtime.json"));
		var store = new PluginRuntimeStateStore(_paths, _logger);

		await store.Save("one", false);
		await store.Save("two", true);
		await store.Remove("one");

		AssertUntouched(path);
	}

	[Test]
	public void Installing_an_extension_keeps_an_unreadable_installation_list_intact()
	{
		var path = Damage(Path.Combine(_paths.StoreDirectory, "installations.json"));
		var store = new JsonStoreInstallationStore(_paths, _logger);

		store.Save(new StoreInstallationRecord
		{
			Origin = "https://store.example",
			Kind = StoreExtensionKind.Plugin,
			PackageId = "one",
			Version = "1.0.0"
		});
		store.Save(new StoreInstallationRecord
		{
			Origin = "https://store.example",
			Kind = StoreExtensionKind.Plugin,
			PackageId = "two",
			Version = "1.0.0"
		});
		store.Delete(StoreExtensionKind.Plugin, "one");

		AssertUntouched(path);
	}

	[Test]
	public void Installing_a_test_build_keeps_an_unreadable_test_installation_list_intact()
	{
		var path = Damage(Path.Combine(_paths.StoreDirectory, "test-installations.json"));
		var store = new JsonStoreTestInstallationStore(_paths, _logger);

		store.Save(TestInstallation("one"));
		store.Save(TestInstallation("two"));
		store.Delete("one");

		AssertUntouched(path);
	}

	[Test]
	public void Refreshing_one_registry_keeps_an_unreadable_registry_state_intact()
	{
		var path = Damage(Path.Combine(_paths.StoreRegistryDirectory, "state.json"));
		var store = new JsonStoreRegistryStateStore(_paths, _logger);

		store.Save(new StoreRegistryState { Origin = "https://one.example" });
		store.Save(new StoreRegistryState { Origin = "https://two.example" });

		AssertUntouched(path);
	}

	[Test]
	public void Cloning_a_secret_keeps_an_unreadable_lineage_intact_and_still_resolves_it_for_the_session()
	{
		var path = Damage(Path.Combine(_paths.DataDirectory, SecretLineageIndex.FileName));
		var lineage = new SecretLineageIndex(_paths, _logger);
		var source = Guid.NewGuid();
		var clone = Guid.NewGuid();

		lineage.RecordClone(source, clone);
		lineage.RecordClone(clone, Guid.NewGuid());

		Assert.That(lineage.Root(clone), Is.EqualTo(source));
		AssertUntouched(path);
	}

	[Test]
	public void Approving_a_flow_keeps_an_unreadable_approval_partition_intact()
	{
		var partitionId = Guid.NewGuid();
		var path = Damage(Path.Combine(_paths.ProfilesDirectory,
			JsonSecretApprovalFiles.DirectoryName,
			partitionId + ".json"));
		var files = new JsonSecretApprovalFiles(_paths, _logger);

		Assert.That(files.LoadAll(SecretApprovalGroup.Profiles).Single().Unreadable, Is.True);

		files.Save(SecretApprovalGroup.Profiles,
			partitionId,
			new SecretApprovalPartition
			{
				Owners = { [Guid.NewGuid()] = new Dictionary<string, SecretApprovalKey>() }
			});
		files.Save(SecretApprovalGroup.Profiles, partitionId, new SecretApprovalPartition());

		AssertUntouched(path);
	}

	[Test]
	public async Task A_plugin_registering_keeps_an_unreadable_capability_snapshot_file_intact()
	{
		var path = Path.Combine(_paths.ConfigDirectory, "plugin-capability-snapshots.json");
		File.WriteAllText(path, Damaged);
		using var store = new RemotePluginSnapshotStore(_paths, _logger);

		await store.SaveAsync(RemotePluginCapabilitySnapshot.Empty("one"));
		await store.RemoveAsync("one");

		Assert.That(File.ReadAllText(path), Is.EqualTo(Damaged));
	}

	[Test]
	public void A_missing_file_is_still_created_on_the_first_save()
	{
		var store = new JsonStoreInstallationStore(_paths, _logger);

		store.Save(new StoreInstallationRecord
		{
			Origin = "https://store.example",
			Kind = StoreExtensionKind.Plugin,
			PackageId = "one",
			Version = "1.0.0"
		});

		Assert.That(store.LoadAll().Single().PackageId, Is.EqualTo("one"));
	}

	private static string Damage(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, Damaged);
		File.WriteAllText(path + DurableJsonFile.BackupSuffix, Damaged);
		return path;
	}

	private static void AssertUntouched(string path)
	{
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(path), Is.EqualTo(Damaged));
			Assert.That(File.ReadAllText(path + DurableJsonFile.BackupSuffix), Is.EqualTo(Damaged));
		});
	}

	private static StoreTestInstallationRecord TestInstallation(string pluginId)
	{
		return new StoreTestInstallationRecord
		{
			PluginId = pluginId,
			Version = "1.0.0",
			Build = "1",
			BuildId = Guid.NewGuid(),
			ArtifactSha256 = new string('0', 64)
		};
	}
}

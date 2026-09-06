using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

[TestFixture]
public class GridInheritanceMigrationTests
{
	[Test]
	public void Normalize_RootMatchingTheDefault_IsClearedAndReportsChanged()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: 3, columns: 5);

		var changed = GridInheritanceMigration.Normalize(profile, [root]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(root.Rows, Is.Null);
			Assert.That(root.Columns, Is.Null);
		});
	}

	[Test]
	public void Normalize_ADifferentGrid_IsKept()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: 6, columns: 8);

		var changed = GridInheritanceMigration.Normalize(profile, [root]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.False);
			Assert.That(root.Rows, Is.EqualTo(6));
			Assert.That(root.Columns, Is.EqualTo(8));
		});
	}

	[Test]
	public void Normalize_ClearsEachAxisIndependently()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: 3, columns: 8); // rows matches the default, columns does not

		GridInheritanceMigration.Normalize(profile, [root]);

		Assert.Multiple(() =>
		{
			Assert.That(root.Rows, Is.Null);
			Assert.That(root.Columns, Is.EqualTo(8));
		});
	}

	[Test]
	public void Normalize_AlreadyInherited_IsANoOp()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: null, columns: null);

		var changed = GridInheritanceMigration.Normalize(profile, [root]);

		Assert.That(changed, Is.False);
	}

	[Test]
	public void InitializeCache_ALegacyProfile_MigratesTheGridAndPersistsItBack()
	{
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "P",
			DefaultRows = 3,
			DefaultColumns = 5,
			Folders =
			[
				new ProfileFolder
				{
					Id = folderId, Name = "Home", Rows = 3, Columns = 5, IsDefault = true, CreatedAt = DateTime.UtcNow
				}
			]
		});
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());

		cache.InitializeCache().GetAwaiter().GetResult();

		var migrated = cache.GetFoldersByProfileId(profileId).Single();
		Assert.Multiple(() =>
		{
			Assert.That(migrated.Rows, Is.Null);
			Assert.That(migrated.Columns, Is.Null);
			Assert.That(store.SaveCount, Is.EqualTo(1));
		});

		cache.Dispose();
	}

	[Test]
	public void InitializeCache_AnAlreadyInheritedProfile_DoesNotPersist()
	{
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "P",
			Folders =
			[
				new ProfileFolder { Id = Guid.NewGuid(), Name = "Home", IsDefault = true, CreatedAt = DateTime.UtcNow }
			]
		});
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());

		cache.InitializeCache().GetAwaiter().GetResult();

		Assert.That(store.SaveCount, Is.EqualTo(0));

		cache.Dispose();
	}

	[Test]
	public void Normalize_NestedFolder_StaysPinnedWhenClearingWouldChangeItsResolvedValue()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: 6, columns: 8);
		var child = Folder(rows: 3, columns: 5, parentId: root.Id);

		var changed = GridInheritanceMigration.Normalize(profile, [root, child]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.False);
			Assert.That(root.Rows, Is.EqualTo(6));
			Assert.That(root.Columns, Is.EqualTo(8));
			Assert.That(child.Rows, Is.EqualTo(3));
			Assert.That(child.Columns, Is.EqualTo(5));
		});
	}

	[Test]
	public void Normalize_BreadthFirst_AWholeMatchingChainClearsInOnePass()
	{
		var profile = Profile(defaultRows: 3, defaultColumns: 5);
		var root = Folder(rows: 3, columns: 5);
		var child = Folder(rows: 3, columns: 5, parentId: root.Id);
		var grandchild = Folder(rows: 3, columns: 5, parentId: child.Id);

		// Deliberately handed in reverse-of-tree order - Normalize must still process parent before child.
		GridInheritanceMigration.Normalize(profile, [grandchild, child, root]);

		Assert.Multiple(() =>
		{
			Assert.That(root.Rows, Is.Null);
			Assert.That(child.Rows, Is.Null);
			Assert.That(grandchild.Rows, Is.Null);
		});
	}

	private static ProfileEntity Profile(int defaultRows, int defaultColumns)
		=> new() { Id = Guid.NewGuid(), Name = "P", DefaultRows = defaultRows, DefaultColumns = defaultColumns };

	private static FolderEntity Folder(int? rows, int? columns, Guid? parentId = null)
		=> new()
		{
			Id = Guid.NewGuid(), Name = "F", Order = 0, ParentId = parentId, Rows = rows, Columns = columns
		};
}

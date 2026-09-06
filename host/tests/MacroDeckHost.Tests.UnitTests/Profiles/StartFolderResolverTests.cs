using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

[TestFixture]
public class StartFolderResolverTests
{
	[Test]
	public void SelectStartFolder_PrefersTheIsDefaultRoot()
	{
		var folders = new List<Folder>
		{
			new() { Id = "a", ParentId = null, Order = 0, IsDefault = false },
			new() { Id = "b", ParentId = null, Order = 1, IsDefault = true },
			new() { Id = "c", ParentId = null, Order = 2, IsDefault = false }
		};

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start?.Id, Is.EqualTo("b"));
	}

	[Test]
	public void SelectStartFolder_IgnoresAnIsDefaultChild()
	{
		var folders = new List<Folder>
		{
			new() { Id = "root-a", ParentId = null, Order = 0, IsDefault = false },
			new() { Id = "root-b", ParentId = null, Order = 1, IsDefault = false },
			new() { Id = "child", ParentId = "root-a", Order = 0, IsDefault = true }
		};

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start?.Id, Is.EqualTo("root-a"));
	}

	[Test]
	public void SelectStartFolder_NoMarker_PicksLowestOrderThenIdOrdinal()
	{
		var folders = new List<Folder>
		{
			new() { Id = "z", ParentId = null, Order = 0, IsDefault = false },
			new() { Id = "a", ParentId = null, Order = 0, IsDefault = false },
			new() { Id = "m", ParentId = null, Order = 1, IsDefault = false }
		};

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start?.Id, Is.EqualTo("a"));
	}

	[Test]
	public void SelectStartFolder_NoMarker_TieBreaksOrdinallyNotByCulture()
	{
		// The case the all-lowercase test above cannot catch: ordinal puts 'B' (0x42) before 'a'
		// (0x61), a culture-aware compare puts "alpha" first - and would silently disagree with the
		// Angular twin FolderService.findStartFolderId.
		var folders = new List<Folder>
		{
			new() { Id = "alpha", ParentId = null, Order = 0, IsDefault = false },
			new() { Id = "Beta", ParentId = null, Order = 0, IsDefault = false }
		};

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start?.Id, Is.EqualTo("Beta"));
	}

	[Test]
	public void SelectStartFolder_VirtualProfileWithNoMarkerAtAll_PicksFirstRootByOrder()
	{
		var folders = new List<Folder>
		{
			new() { Id = "obs::folder-2", ParentId = null, Order = 1, IsDefault = false },
			new() { Id = "obs::folder-1", ParentId = null, Order = 0, IsDefault = false }
		};

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start?.Id, Is.EqualTo("obs::folder-1"));
	}

	[Test]
	public void SelectStartFolder_NoRoots_ReturnsNull()
	{
		var folders = new List<Folder> { new() { Id = "child", ParentId = "missing-parent", Order = 0 } };

		var start = StartFolderResolver.SelectStartFolder(folders);

		Assert.That(start, Is.Null);
	}

	[Test]
	public void Resolve_UnknownProfileId_ReturnsNull()
	{
		var registry = new FakeProfileRegistry();

		var start = StartFolderResolver.Resolve(registry, "does-not-exist");

		Assert.That(start, Is.Null);
	}

	[Test]
	public void Resolve_KnownProfileId_ReturnsItsStartFolder()
	{
		var registry = new FakeProfileRegistry()
			.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });

		var start = StartFolderResolver.Resolve(registry, "p1");

		Assert.That(start?.Id, Is.EqualTo("f1"));
	}
}

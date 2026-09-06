using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Common;

[TestFixture]
public class FolderSubtreeReachTests
{
	private FolderEntity _main = null!;
	private FolderEntity _games = null!;
	private FolderEntity _retro = null!;
	private FolderEntity _media = null!;
	private FolderEntity _work = null!;
	private FolderEntity _mail = null!;
	private List<FolderEntity> _tree = null!;

	[SetUp]
	public void SetUp()
	{
		_main = Folder("Main", null);
		_games = Folder("Games", _main.Id);
		_retro = Folder("Retro", _games.Id);
		_media = Folder("Media", _main.Id);
		_work = Folder("Work", null);
		_mail = Folder("Mail", _work.Id);
		_tree = [_main, _games, _retro, _media, _work, _mail];
	}


	[Test]
	public void Collect_Main_IsExactlyMainGamesRetroMedia()
	{
		var result = FolderSubtree.Collect(_tree, _main);

		Assert.That(result.Select(f => f.Id), Is.EquivalentTo(new[] { _main.Id, _games.Id, _retro.Id, _media.Id }));
	}

	[Test]
	public void Collect_Games_IsExactlyGamesRetro()
	{
		var result = FolderSubtree.Collect(_tree, _games);

		Assert.That(result.Select(f => f.Id), Is.EquivalentTo(new[] { _games.Id, _retro.Id }));
	}

	[Test]
	public void Collect_Retro_IsExactlyRetro()
	{
		var result = FolderSubtree.Collect(_tree, _retro);

		Assert.That(result.Select(f => f.Id), Is.EquivalentTo(new[] { _retro.Id }));
	}

	[Test]
	public void Collect_Mail_IsExactlyMail()
	{
		var result = FolderSubtree.Collect(_tree, _mail);

		Assert.That(result.Select(f => f.Id), Is.EquivalentTo(new[] { _mail.Id }));
	}

	[Test]
	public void AncestorsAndSelf_Retro_IsExactlyRetroGamesMain()
	{
		var result = FolderSubtree.AncestorsAndSelf(_tree, _retro);

		Assert.That(result, Is.EquivalentTo(new[] { _retro.Id, _games.Id, _main.Id }));
	}

	[Test]
	public void AncestorsAndSelf_Media_IsExactlyMediaMain()
	{
		var result = FolderSubtree.AncestorsAndSelf(_tree, _media);

		Assert.That(result, Is.EquivalentTo(new[] { _media.Id, _main.Id }));
	}

	[Test]
	public void AncestorsAndSelf_Main_IsExactlyMain()
	{
		var result = FolderSubtree.AncestorsAndSelf(_tree, _main);

		Assert.That(result, Is.EquivalentTo(new[] { _main.Id }));
	}

	[Test]
	public void AncestorsAndSelf_Mail_IsExactlyMailWork()
	{
		var result = FolderSubtree.AncestorsAndSelf(_tree, _mail);

		Assert.That(result, Is.EquivalentTo(new[] { _mail.Id, _work.Id }));
	}


	[Test]
	public void DownwardAndUpwardReach_AgreeForEveryHomeTargetPair()
	{
		foreach (var home in _tree)
		{
			var collected = FolderSubtree.Collect(_tree, home).Select(f => f.Id).ToHashSet();
			foreach (var target in _tree)
			{
				var ancestors = FolderSubtree.AncestorsAndSelf(_tree, target);
				Assert.That(collected.Contains(target.Id),
					Is.EqualTo(ancestors.Contains(home.Id)),
					$"Collect({home.Name}) vs AncestorsAndSelf({target.Name}) disagree");
			}
		}
	}


	[Test]
	public void AncestorsAndSelf_DanglingParentId_YieldsOnlyItself()
	{
		var orphan = Folder("Orphan", Guid.NewGuid()); // parent id not present in the list
		var tree = new List<FolderEntity>(_tree) { orphan };

		var result = FolderSubtree.AncestorsAndSelf(tree, orphan);

		Assert.That(result, Is.EquivalentTo(new[] { orphan.Id }));
	}

	[Test]
	public void Collect_DanglingParentId_IsNotReachedByAnyOtherFoldersSubtree()
	{
		var orphan = Folder("Orphan", Guid.NewGuid());
		var tree = new List<FolderEntity>(_tree) { orphan };

		foreach (var candidateHome in tree.Where(f => f.Id != orphan.Id))
		{
			var collected = FolderSubtree.Collect(tree, candidateHome).Select(f => f.Id);
			Assert.That(collected, Does.Not.Contain(orphan.Id));
		}
	}


	[Test]
	public void AncestorsAndSelf_TwoFolderCycle_CoversBothAndTerminates()
	{
		var a = Folder("A", null);
		var b = Folder("B", a.Id);
		a.ParentId = b.Id; // A -> B -> A
		var tree = new List<FolderEntity> { a, b };

		var fromA = FolderSubtree.AncestorsAndSelf(tree, a);
		var fromB = FolderSubtree.AncestorsAndSelf(tree, b);

		Assert.Multiple(() =>
		{
			Assert.That(fromA, Is.EquivalentTo(new[] { a.Id, b.Id }));
			Assert.That(fromB, Is.EquivalentTo(new[] { a.Id, b.Id }));
		});
	}

	[Test]
	public void AncestorsAndSelf_SelfParentCycle_YieldsOnlyItself()
	{
		var a = Folder("A", null);
		a.ParentId = a.Id; // A -> A
		var tree = new List<FolderEntity> { a };

		var result = FolderSubtree.AncestorsAndSelf(tree, a);

		Assert.That(result, Is.EquivalentTo(new[] { a.Id }));
	}

	[Test]
	public void Collect_TwoFolderCycle_CoversBothAndTerminates()
	{
		var a = Folder("A", null);
		var b = Folder("B", a.Id);
		a.ParentId = b.Id; // A -> B -> A
		var tree = new List<FolderEntity> { a, b };

		var fromA = FolderSubtree.Collect(tree, a).Select(f => f.Id);
		var fromB = FolderSubtree.Collect(tree, b).Select(f => f.Id);

		Assert.Multiple(() =>
		{
			Assert.That(fromA, Is.EquivalentTo(new[] { a.Id, b.Id }));
			Assert.That(fromB, Is.EquivalentTo(new[] { a.Id, b.Id }));
		});
	}

	[Test]
	public void Collect_SelfParentCycle_YieldsOnlyItself()
	{
		var a = Folder("A", null);
		a.ParentId = a.Id; // A -> A
		var tree = new List<FolderEntity> { a };

		var result = FolderSubtree.Collect(tree, a).Select(f => f.Id);

		Assert.That(result, Is.EquivalentTo(new[] { a.Id }));
	}

	private static FolderEntity Folder(string name, Guid? parentId) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Order = 0,
		ParentId = parentId
	};
}

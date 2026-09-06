using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconImportCoalescerTests
{
	private static readonly SourceContentHash _hash = SourceContentHash.Compute("logo"u8);

	[Test]
	public void TryReserve_FirstCallerWins_AndLaterOnesGetTheWinner()
	{
		var coalescer = new IconImportCoalescer();
		var first = Guid.CreateVersion7();
		var second = Guid.CreateVersion7();

		Assert.Multiple(() =>
		{
			Assert.That(coalescer.TryReserve(IconImportCoalescer.CatalogWideScope, _hash, first), Is.Null);
			Assert.That(coalescer.TryReserve(IconImportCoalescer.CatalogWideScope, _hash, second),
				Is.EqualTo(first));
		});
	}

	// The user's rule for explicit pack imports: an item requested for one pack must never resolve to an
	// icon belonging to another, not even when both imports race on the same bytes.
	[Test]
	public void TryReserve_KeepsScopesApart()
	{
		var coalescer = new IconImportCoalescer();
		var packA = Guid.CreateVersion7();
		var packB = Guid.CreateVersion7();
		var iconInA = Guid.CreateVersion7();
		var iconInB = Guid.CreateVersion7();

		Assert.Multiple(() =>
		{
			Assert.That(coalescer.TryReserve(packA, _hash, iconInA), Is.Null);
			Assert.That(coalescer.TryReserve(packB, _hash, iconInB), Is.Null);
			Assert.That(coalescer.TryReserve(packA, _hash, Guid.CreateVersion7()), Is.EqualTo(iconInA));
			Assert.That(coalescer.TryReserve(packB, _hash, Guid.CreateVersion7()), Is.EqualTo(iconInB));
		});
	}

	[Test]
	public void ConcurrentReservations_ConvergeOnOneIcon()
	{
		var coalescer = new IconImportCoalescer();
		var candidates = Enumerable.Range(0, 200).Select(_ => Guid.CreateVersion7()).ToArray();
		var outcomes = new Guid?[candidates.Length];

		Parallel.For(0,
			candidates.Length,
			i => outcomes[i] = coalescer.TryReserve(IconImportCoalescer.CatalogWideScope, _hash, candidates[i]));

		var winners = candidates.Where((_, i) => outcomes[i] is null).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(winners, Has.Count.EqualTo(1), "exactly one import may create the icon");
			Assert.That(outcomes.Where(outcome => outcome is not null).Distinct().Count(),
				Is.EqualTo(1),
				"every loser is pointed at the same winner");
			Assert.That(outcomes.First(outcome => outcome is not null), Is.EqualTo(winners[0]));
		});
	}

	// A failed conversion must not leave its content permanently claimed, or the bytes could never be
	// imported again.
	[Test]
	public void Release_LetsTheNextImportClaimTheContent()
	{
		var coalescer = new IconImportCoalescer();
		var failed = Guid.CreateVersion7();
		var retry = Guid.CreateVersion7();

		coalescer.TryReserve(IconImportCoalescer.CatalogWideScope, _hash, failed);
		coalescer.ReleaseAll(failed);

		Assert.That(coalescer.TryReserve(IconImportCoalescer.CatalogWideScope, _hash, retry), Is.Null);
	}
}

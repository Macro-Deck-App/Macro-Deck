using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableWidgetPlacementTests
{
	[Test]
	public void Place_SingleWidget_AtFreeAnchor_UsesTheAnchor()
	{
		var group = new List<PortableCell> { new(0, 0, 1, 1) };

		var placed = PortableWidgetPlacement.Place(group, 5, 3, [], 2, 1);

		Assert.That(placed, Is.EqualTo(new List<(int, int)> { (2, 1) }));
	}

	[Test]
	public void Place_WhenAnchorOccupied_FindsTheNextFreeCell()
	{
		var group = new List<PortableCell> { new(0, 0, 1, 1) };
		var occupied = new List<PortableCell> { new(0, 0, 1, 1) };

		var placed = PortableWidgetPlacement.Place(group, 2, 1, occupied, 0, 0);

		Assert.That(placed, Is.EqualTo(new List<(int, int)> { (1, 0) }));
	}

	[Test]
	public void Place_PreservesRelativeLayoutOfAGroup()
	{
		var group = new List<PortableCell> { new(0, 0, 1, 1), new(1, 1, 1, 1) };

		var placed = PortableWidgetPlacement.Place(group, 4, 4, [], 1, 1);

		Assert.That(placed, Is.EqualTo(new List<(int, int)> { (1, 1), (2, 2) }));
	}

	[Test]
	public void Place_WhenGroupCannotFit_ReturnsNull()
	{
		var group = new List<PortableCell> { new(0, 0, 2, 2) };

		var placed = PortableWidgetPlacement.Place(group, 1, 1, [], 0, 0);

		Assert.That(placed, Is.Null);
	}

	[Test]
	public void Place_WhenGridFull_ReturnsNull()
	{
		var group = new List<PortableCell> { new(0, 0, 1, 1) };
		var occupied = new List<PortableCell> { new(0, 0, 1, 1) };

		var placed = PortableWidgetPlacement.Place(group, 1, 1, occupied, 0, 0);

		Assert.That(placed, Is.Null);
	}
}

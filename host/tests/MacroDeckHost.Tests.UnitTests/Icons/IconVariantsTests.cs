using MacroDeckHost.Application.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconVariantsTests
{
	[Test]
	public void Resolve_NoRequestedSize_ReturnsMaster()
	{
		Assert.That(IconVariants.Resolve(null, [128, 256]), Is.EqualTo(IconVariants.Master));
	}

	[Test]
	public void Resolve_NoVariantsAvailable_ReturnsMaster()
	{
		Assert.That(IconVariants.Resolve(128, []), Is.EqualTo(IconVariants.Master));
	}

	[Test]
	public void Resolve_ExactMatch_ReturnsThatSize()
	{
		Assert.That(IconVariants.Resolve(256, [128, 256, 512]), Is.EqualTo("256"));
	}

	[Test]
	public void Resolve_BetweenSizes_ReturnsNextLarger()
	{
		Assert.That(IconVariants.Resolve(200, [128, 256, 512]), Is.EqualTo("256"));
	}

	[Test]
	public void Resolve_SmallerThanAll_ReturnsSmallest()
	{
		Assert.That(IconVariants.Resolve(64, [128, 256]), Is.EqualTo("128"));
	}

	[Test]
	public void Resolve_LargerThanAllVariants_FallsBackToMaster()
	{
		Assert.That(IconVariants.Resolve(1024, [128, 256]), Is.EqualTo(IconVariants.Master));
	}
}

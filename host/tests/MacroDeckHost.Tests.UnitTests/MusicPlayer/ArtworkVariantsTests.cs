using MacroDeckHost.Application.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class ArtworkVariantsTests
{
	[Test]
	public void Resolve_NoRequestedSize_ReturnsMaster()
	{
		Assert.That(ArtworkVariants.Resolve(null, [128, 256]), Is.Null);
	}

	[Test]
	public void Resolve_NoVariantsAvailable_ReturnsMaster()
	{
		Assert.That(ArtworkVariants.Resolve(128, []), Is.Null);
	}

	[Test]
	public void Resolve_ExactMatch_ReturnsThatSize()
	{
		Assert.That(ArtworkVariants.Resolve(256, [128, 256]), Is.EqualTo(256));
	}

	[Test]
	public void Resolve_BetweenSizes_ReturnsNextLarger()
	{
		Assert.That(ArtworkVariants.Resolve(200, [128, 256]), Is.EqualTo(256));
	}

	[Test]
	public void Resolve_SmallerThanAll_ReturnsSmallest()
	{
		Assert.That(ArtworkVariants.Resolve(64, [128, 256]), Is.EqualTo(128));
	}

	[Test]
	public void Resolve_LargerThanAllVariants_FallsBackToMaster()
	{
		Assert.That(ArtworkVariants.Resolve(512, [128, 256]), Is.Null);
	}
}

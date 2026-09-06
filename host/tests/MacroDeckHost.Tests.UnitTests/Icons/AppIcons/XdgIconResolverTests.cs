using MacroDeckHost.Infrastructure.Icons.AppIcons;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class XdgIconResolverTests
{
	private string _root = null!;

	private string[] _roots = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_roots = [Path.Combine(_root, "does-not-exist"), _root];
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	[Test]
	public void TryResolve_SeveralRasterSizes_PicksTheLargestAndIgnoresFileLength()
	{
		CreateFile("hicolor/48x48/apps/sized.png", 4096);
		var expected = CreateFile("hicolor/256x256/apps/sized.png", 16);

		Assert.That(XdgIconResolver.TryResolve("sized", _roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_SvgAndLargerRaster_PicksTheSvg()
	{
		CreateFile("hicolor/48x48/apps/both.png", 64);
		CreateFile("hicolor/256x256/apps/both.png", 64);
		var expected = CreateFile("pixmaps/both.svg", 8);

		Assert.That(XdgIconResolver.TryResolve("both", _roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_RasterInScalableDirectory_RanksWithSvg()
	{
		CreateFile("hicolor/256x256/apps/vector.png", 64);
		var expected = CreateFile("hicolor/scalable/apps/vector.png", 64);

		Assert.That(XdgIconResolver.TryResolve("vector", _roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_HiDpiDirectorySegment_CountsPhysicalPixels()
	{
		CreateFile("hicolor/192x192/apps/hidpi.png", 64);
		var expected = CreateFile("hicolor/128x128@2x/apps/hidpi.png", 64);

		Assert.That(XdgIconResolver.TryResolve("hidpi", _roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_UnparsableDirectorySegment_FallsBackToFileLength()
	{
		CreateFile("pixmaps/unsized.png", 16);
		var expected = CreateFile("legacy-theme/apps/unsized.png", 512);

		Assert.That(XdgIconResolver.TryResolve("unsized", _roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_XpmAloneAndAlongsideAPng_OnlyWinsWhenAlone()
	{
		var alone = CreateFile("pixmaps/legacy.xpm", 64);
		CreateFile("hicolor/256x256/apps/mixed.xpm", 4096);
		var png = CreateFile("hicolor/48x48/apps/mixed.png", 16);

		Assert.Multiple(() =>
		{
			Assert.That(XdgIconResolver.TryResolve("legacy", _roots), Is.EqualTo(alone));
			Assert.That(XdgIconResolver.TryResolve("mixed", _roots), Is.EqualTo(png));
		});
	}

	[Test]
	public void TryResolve_ExtensionCasing_IsIgnoredButStemMustMatchExactly()
	{
		var upper = CreateFile("hicolor/48x48/apps/upper.PNG", 16);
		CreateFile("hicolor/48x48/apps/prefix-suffix.png", 16);
		CreateFile("hicolor/48x48/apps/Cased.png", 16);

		Assert.Multiple(() =>
		{
			Assert.That(XdgIconResolver.TryResolve("upper", _roots), Is.EqualTo(upper));
			Assert.That(XdgIconResolver.TryResolve("prefix", _roots), Is.Null);
			Assert.That(XdgIconResolver.TryResolve("cased", _roots), Is.Null);
		});
	}

	[Test]
	public void TryResolve_PathLikeIconName_RoundTripsWhenTheFileExists()
	{
		var file = CreateFile("hicolor/48x48/apps/absolute.png", 16);
		var missing = Path.Combine(_root, "hicolor", "48x48", "apps", "gone.png");

		Assert.Multiple(() =>
		{
			Assert.That(XdgIconResolver.TryResolve(file, []), Is.EqualTo(file));
			Assert.That(XdgIconResolver.TryResolve(missing, _roots), Is.Null);
		});
	}

	[Test]
	public void TryResolve_UnknownNameOrBlankInput_ReturnsNull()
	{
		CreateFile("hicolor/48x48/apps/present.png", 16);

		Assert.Multiple(() =>
		{
			Assert.That(XdgIconResolver.TryResolve("absent", _roots), Is.Null);
			Assert.That(XdgIconResolver.TryResolve("   ", _roots), Is.Null);
			Assert.That(XdgIconResolver.TryResolve("present", []), Is.Null);
		});
	}

	[Test]
	public void TryResolve_MissingRoot_DoesNotThrow()
	{
		var expected = CreateFile("hicolor/48x48/apps/robust.png", 16);
		string[] roots = [Path.Combine(_root, "nope"), string.Empty, _root, _root];

		Assert.That(XdgIconResolver.TryResolve("robust", roots), Is.EqualTo(expected));
	}

	[Test]
	public void TryResolve_IconBelowTheDepthBound_IsNotReturned()
	{
		var deep = Path.Combine("deep", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
		CreateFile(Path.Combine(deep, "too-deep.png"), 16);

		Assert.That(XdgIconResolver.TryResolve("too-deep", _roots), Is.Null);
	}

	[Test]
	public void DefaultSearchRoots_WithoutXdgEnvironment_ReturnsUniqueSystemRoots()
	{
		var roots = XdgIconResolver.DefaultSearchRoots();

		Assert.Multiple(() =>
		{
			Assert.That(roots, Is.Not.Empty);
			Assert.That(roots, Is.Unique);
			Assert.That(roots, Contains.Item("/usr/share/icons"));
			Assert.That(roots, Contains.Item("/usr/share/pixmaps"));
		});
	}

	private string CreateFile(string relativePath, int length)
	{
		var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, new byte[length]);
		return path;
	}
}

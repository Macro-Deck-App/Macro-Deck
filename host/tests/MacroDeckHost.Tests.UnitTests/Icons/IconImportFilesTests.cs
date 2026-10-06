using MacroDeckHost.Application.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconImportFilesTests
{
	[TestCase("spinner.lottie")]
	[TestCase("loader.json")]
	[TestCase("logo.png")]
	public void IsImportableIcon_AcceptsExplicitlyPickedFiles(string fileName)
	{
		Assert.That(IconImportFiles.IsImportableIcon(fileName), Is.True);
	}

	[TestCase("notes.txt")]
	[TestCase("pack.zip")]
	public void IsImportableIcon_RejectsEverythingElse(string fileName)
	{
		Assert.That(IconImportFiles.IsImportableIcon(fileName), Is.False);
	}

	[Test]
	public void IsSupportedImportEntry_IgnoresJsonInsideContainers()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconImportFiles.IsSupportedImportEntry("manifest.json"), Is.False);
			Assert.That(IconImportFiles.IsSupportedImportEntry("icons/spinner.lottie"), Is.True);
			Assert.That(IconImportFiles.IsSupportedImportEntry("icons/star.png"), Is.True);
		});
	}

	[Test]
	public void IsUploadedIcon_TakesJsonOnlyWhenItWasPickedByHand()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconImportFiles.IsUploadedIcon("loader.json"), Is.True);
			Assert.That(IconImportFiles.IsUploadedIcon("assets/package.json"), Is.False);
			Assert.That(IconImportFiles.IsUploadedIcon("assets/spinner.lottie"), Is.True);
			Assert.That(IconImportFiles.IsUploadedIcon(@"assets\tsconfig.json"), Is.False);
		});
	}

	[Test]
	public void IsIconDropSource_TakesLottieButNotJson()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconImportFiles.IsIconDropSource("/tmp/spinner.lottie"), Is.True);
			Assert.That(IconImportFiles.IsIconDropSource("/tmp/tsconfig.json"), Is.False);
		});
	}

	[Test]
	public void IsArchive_DoesNotClaimLottieContainers()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconImportFiles.IsArchive("spinner.lottie"), Is.False);
			Assert.That(IconImportFiles.IsPackFile("spinner.lottie"), Is.False);
			Assert.That(IconImportFiles.IsLottieContainer("spinner.lottie"), Is.True);
		});
	}

	[TestCase("a.png")]
	[TestCase("a.svg")]
	[TestCase("a.webp")]
	[TestCase("spinner.lottie")]
	[TestCase("Tool.exe")]
	[TestCase("S.lnk")]
	[TestCase("S.url")]
	[TestCase("E.desktop")]
	[TestCase("I.ico")]
	[TestCase("S.icns")]
	[TestCase("Spotify.app")]
	[TestCase("Spotify.app/")]
	[TestCase("icons.zip")]
	public void IsFolderImportEntry_AcceptsTheSweepableSet(string fileName)
	{
		Assert.That(IconImportFiles.IsFolderImportEntry(fileName), Is.True);
	}

	[TestCase("R.dll")]
	[TestCase("readme.md")]
	[TestCase("package.json")]
	public void IsFolderImportEntry_RejectsEverythingElse(string fileName)
	{
		Assert.That(IconImportFiles.IsFolderImportEntry(fileName), Is.False);
	}

	[TestCase("play.dark.png", "play", "colorScheme=dark")]
	[TestCase("sub/play.Static.GIF", "play", "motion=static")]
	[TestCase("play.static.dark.webp", "play", "colorScheme=dark;motion=static")]
	[TestCase("my.icon.animated.light.png", "my.icon", "colorScheme=light;motion=animated")]
	public void A_known_suffix_names_an_appearance_of_the_base_name(string fileName, string baseName, string key)
	{
		Assert.Multiple(() =>
		{
			Assert.That(IconImportFiles.TryParseAppearanceName(fileName, out var parsedBase, out var traits), Is.True);
			Assert.That(parsedBase, Is.EqualTo(baseName));
			Assert.That(IconAppearanceTraits.ToKey(traits), Is.EqualTo(key));
		});
	}

	[TestCase("play.png")]
	[TestCase("play.blue.png")]
	[TestCase("dark.png")]
	[TestCase("play.dark.light.png")]
	[TestCase("play.dark.blue.png")]
	public void Anything_else_is_a_plain_file_name(string fileName)
	{
		Assert.That(IconImportFiles.TryParseAppearanceName(fileName, out _, out _), Is.False);
	}
}

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
}

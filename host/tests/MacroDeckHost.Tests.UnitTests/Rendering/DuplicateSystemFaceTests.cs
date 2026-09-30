using MacroDeckHost.Infrastructure.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class DuplicateSystemFaceTests
{
	private const string Family = "Fixture Sans";

	[Test]
	public void GetFaces_SymlinkedCopiesOfOneFace_ListTheFaceOnceUnderItsFirstId()
	{
		var catalog = Catalog(
			Regular("A"),
			Regular("A"),
			Regular("A"),
			Italic("B"),
			Italic("B"));

		var faces = catalog.GetFaces();

		Assert.Multiple(() =>
		{
			Assert.That(faces.Select(face => face.FaceId).ToList(),
				Is.EqualTo(new[] { "fixture-sans-400-5-upright", "fixture-sans-400-5-italic" }));
			Assert.That(faces.Select(face => face.StyleName).ToList(), Is.EqualTo(new[] { "Regular", "Italic" }));
		});
	}

	[Test]
	public void GetFaces_TwoFontsWithTheSameStyleButDifferentContent_BothStaySelectable()
	{
		var faces = Catalog(Regular("A"), Regular("B")).GetFaces();

		Assert.That(faces.Select(face => face.FaceId).ToList(),
			Is.EqualTo(new[] { "fixture-sans-400-5-upright", "fixture-sans-400-5-upright-2" }));
	}

	[Test]
	public void GetFaces_AFaceWithoutAFingerprint_IsNeverTreatedAsADuplicate()
	{
		var faces = Catalog(Regular(null), Regular(null), Regular("A")).GetFaces();

		Assert.That(faces, Has.Count.EqualTo(3));
	}

	[Test]
	public void GetFaces_ACopyListedAfterADifferentFaceOfTheSameStyle_IsStillRecognisedAsACopy()
	{
		var catalog = Catalog(Regular("A"), Regular("B"), Regular("A"));

		Assert.Multiple(() =>
		{
			Assert.That(catalog.GetFaces().Select(face => face.FaceId).ToList(),
				Is.EqualTo(new[] { "fixture-sans-400-5-upright", "fixture-sans-400-5-upright-2" }));
			Assert.That(catalog.ResolveFaceId("fixture-sans-400-5-upright-3"), Is.EqualTo("fixture-sans-400-5-upright"));
		});
	}

	[Test]
	public void ResolveFaceId_MapsAHiddenDuplicateToItsListedTwinAndLeavesOtherIdsAlone()
	{
		var catalog = Catalog(Regular("A"), Regular("A"), Italic("B"));

		Assert.Multiple(() =>
		{
			Assert.That(catalog.ResolveFaceId("fixture-sans-400-5-upright-2"), Is.EqualTo("fixture-sans-400-5-upright"));
			Assert.That(catalog.ResolveFaceId("fixture-sans-400-5-italic"), Is.EqualTo("fixture-sans-400-5-italic"));
			Assert.That(catalog.ResolveFaceId("no-such-face"), Is.EqualTo("no-such-face"));
		});
	}

	[Test]
	public void GetFaces_InstalledFaceSeenAgain_IsHiddenButItsIdKeepsServingTheFile()
	{
		var family = FindExtractableFamily();
		if (family is null)
		{
			Assert.Ignore("No installed family has a face whose tables can be read.");
		}

		IEnumerable<SkiaFontCatalog.SystemFaceEntry> Installed() =>
			SkiaFontCatalog.EnumerateSystemFaces().Where(entry => entry.Family == family);

		var baseline = new SkiaFontCatalog([], Installed);
		var seenTwice = new SkiaFontCatalog([], () => Installed().SelectMany(entry => new[] { entry, entry }));

		var listedIds = seenTwice.GetFaces().Select(face => face.FaceId).ToList();
		var hiddenIds = listedIds
			.SelectMany(id => Enumerable.Range(2, 40).Select(suffix => $"{id}-{suffix}"))
			.Where(id => seenTwice.ResolveFaceId(id) != id)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(listedIds, Is.EqualTo(baseline.GetFaces().Select(face => face.FaceId).ToList()));
			Assert.That(hiddenIds, Is.Not.Empty);
			Assert.That(hiddenIds.Where(id => seenTwice.GetFaceFile(id) is null), Is.Empty);
		});
	}

	private static string? FindExtractableFamily()
	{
		foreach (var entry in SkiaFontCatalog.EnumerateSystemFaces())
		{
			if (entry.RemoteRenderable && entry.Fingerprint() is not null)
			{
				return entry.Family;
			}
		}

		return null;
	}

	private static SkiaFontCatalog Catalog(params SkiaFontCatalog.SystemFaceEntry[] entries) =>
		new([], () => entries);

	private static SkiaFontCatalog.SystemFaceEntry Regular(string? fingerprint) =>
		Entry(400, SKFontStyleSlant.Upright, () => fingerprint);

	private static SkiaFontCatalog.SystemFaceEntry Italic(string? fingerprint) =>
		Entry(400, SKFontStyleSlant.Italic, () => fingerprint);

	private static SkiaFontCatalog.SystemFaceEntry Entry(int weight, SKFontStyleSlant slant, Func<string?> fingerprint) =>
		new(Family, 0, weight, 5, slant, true, fingerprint);
}

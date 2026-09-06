using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Infrastructure.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public partial class SystemFontCatalogTests
{
	private const uint TrueTypeSignature = 0x00010000;
	private const uint CffSignature = 0x4F54544F;
	private const uint AppleTrueTypeSignature = 0x74727565;
	private const uint CollectionSignature = 0x74746366;

	[Test]
	public void Extract_AFaceAtANonZeroCollectionIndex_YieldsThatFaceAsAStandaloneFont()
	{
		var fixture = FindCollectionFixture();
		if (fixture is null)
		{
			Assert.Ignore("No installed family exposes a regular and a bold face that are distinct fonts.");
		}

		using var styles = SKFontManager.Default.GetFontStyles(fixture.Family);
		var collection = BuildCollection(ExtractStandalone(styles, fixture.RegularIndex),
			ExtractStandalone(styles, fixture.BoldIndex));

		using var atIndexZero = SKTypeface.FromStream(new MemoryStream(collection), index: 0);
		Assert.That(atIndexZero, Is.Not.Null, "the synthetic collection must itself be a readable font collection");

		using var bold = SKTypeface.FromStream(new MemoryStream(collection), index: 1);
		if (bold is null)
		{
			Assert.Ignore("This platform's Skia backend cannot open a font collection at a non-zero index.");
		}

		var extracted = SfntFaceExtractor.Extract(bold);
		Assert.That(extracted, Is.Not.Null, "a face inside a collection must still be servable");

		var signature = BinaryPrimitives.ReadUInt32BigEndian(extracted);
		using var reparsed = SKTypeface.FromStream(new MemoryStream(extracted));

		Assert.Multiple(() =>
		{
			Assert.That(signature, Is.Not.EqualTo(CollectionSignature), "the container must not be served as-is");
			Assert.That(signature, Is.AnyOf(TrueTypeSignature, CffSignature, AppleTrueTypeSignature));
			Assert.That(reparsed, Is.Not.Null);
			Assert.That(reparsed!.FamilyName, Is.EqualTo(fixture.Family));
			Assert.That(reparsed.FontWeight, Is.EqualTo(BoldWeight), "index 1 must not fall back to the index 0 face");
		});
	}

	[Test]
	public void GetFaceFile_AgreesWithTheAdvertisedRemoteRenderableFlagForEveryFace()
	{
		var catalog = new SkiaFontCatalog();
		var faces = catalog.GetFaces();

		var mismatched = faces
			.Where(face => face.RemoteRenderable != (catalog.GetFaceFile(face.FaceId) is not null))
			.Select(face => $"{face.FaceId} (remoteRenderable={face.RemoteRenderable})")
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(faces, Is.Not.Empty, "the platform catalog must expose at least one face");
			Assert.That(mismatched, Is.Empty, "an advertised face must be servable, and only a servable one");
		});
	}

	[Test]
	public void GetFaces_BuiltTwice_ProducesTheSameFaceIdsForTheSameFaces()
	{
		var first = new SkiaFontCatalog().GetFaces();
		var second = new SkiaFontCatalog().GetFaces();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.Empty);
			Assert.That(second.Select(face => face.FaceId).ToList(),
				Is.EqualTo(first.Select(face => face.FaceId).ToList()));
			Assert.That(second.Select(face => face.Family).ToList(),
				Is.EqualTo(first.Select(face => face.Family).ToList()));
		});
	}

	[Test]
	public void GetFaces_CoversEveryFamilyTheFontManagerReports()
	{
		var families = SKFontManager.Default.FontFamilies
			.Where(family => !string.IsNullOrWhiteSpace(family))
			.ToList();
		var covered = new SkiaFontCatalog().GetFaces()
			.Select(face => face.Family)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Assert.Multiple(() =>
		{
			Assert.That(families, Is.Not.Empty);
			Assert.That(families.Where(family => !covered.Contains(family)), Is.Empty);
		});
	}

	[Test]
	public void GetFaces_FaceIds_AreUrlSafeAndCarryTheFaceAttributes()
	{
		var faces = new SkiaFontCatalog().GetFaces();

		var unsafeIds = faces.Where(face => !FaceIdPattern().IsMatch(face.FaceId)).Select(face => face.FaceId).ToList();
		var unattributed = faces
			.Where(face =>
			{
				var expected = $"-{face.Weight}-{face.Width}-{face.Slant}";
				return !face.FaceId.EndsWith(expected, StringComparison.Ordinal) &&
					!face.FaceId.Contains($"{expected}-", StringComparison.Ordinal);
			})
			.Select(face => face.FaceId)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(faces, Is.Not.Empty);
			Assert.That(unsafeIds, Is.Empty);
			Assert.That(unattributed, Is.Empty);
			Assert.That(faces.Select(face => face.FaceId).ToHashSet(StringComparer.Ordinal),
				Has.Count.EqualTo(faces.Count));
		});
	}

	[Test]
	public void BuildLegacy_ForEveryInstalledRegularFace_ProducesTheIdTheCatalogMinted()
	{
		var faces = new SkiaFontCatalog().GetFaces();
		// Faces sharing all four id components get a deterministic numeric suffix, which no
		// attribute-derived id can predict - and does not need to, since the migration only builds an id
		// itself for a family this machine does not have.
		var regulars = faces
			.Where(face =>
				face.Weight == RegularWeight &&
				face.Width == FontFaceIdentity.NormalWidth &&
				face.Slant == FontFaceIdentity.UprightSlant)
			.GroupBy(face => face.Family, StringComparer.Ordinal)
			.Where(group => group.Count() == 1)
			.Select(group => group.Single())
			.ToList();

		// A family name that is already a bare slug cannot tell two slug implementations apart, so the
		// check is only meaningful while some installed family carries punctuation or spacing.
		var punctuated = regulars.Where(face => face.Family.Any(character => !char.IsAsciiLetterOrDigit(character)))
			.ToList();

		var disagreeing = regulars
			.Where(face => !string.Equals(FontFaceIdentity.BuildLegacy(face.Family, bold: false, italic: false),
				face.FaceId,
				StringComparison.Ordinal))
			.Select(face => $"{face.Family} -> {face.FaceId}")
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(regulars, Is.Not.Empty);
			Assert.That(punctuated, Is.Not.Empty);
			Assert.That(disagreeing, Is.Empty);
		});
	}

	[GeneratedRegex("^[a-z0-9-]+$")]
	private static partial Regex FaceIdPattern();

	private const int RegularWeight = 400;
	private const int BoldWeight = 700;
	private const int NormalWidth = 5;

	private sealed record CollectionFixture(string Family, int RegularIndex, int BoldIndex);

	private static CollectionFixture? FindCollectionFixture()
	{
		var manager = SKFontManager.Default;
		foreach (var family in manager.FontFamilies.Where(name => !string.IsNullOrWhiteSpace(name)))
		{
			using var styles = manager.GetFontStyles(family);
			var regular = IndexOfStyle(styles, RegularWeight);
			var bold = IndexOfStyle(styles, BoldWeight);
			if (regular < 0 || bold < 0)
			{
				continue;
			}

			// A variable font reports both weights but carries one set of tables, which would make the
			// collection's two members identical and the index-1 assertion meaningless.
			if (WeightOf(ExtractStandalone(styles, regular)) != RegularWeight ||
				WeightOf(ExtractStandalone(styles, bold)) != BoldWeight)
			{
				continue;
			}

			return new CollectionFixture(family, regular, bold);
		}

		return null;
	}

	private static int IndexOfStyle(SKFontStyleSet styles, int weight)
	{
		for (var index = 0; index < styles.Count; index++)
		{
			using var style = styles[index];
			if (style.Weight == weight && style.Width == NormalWidth && style.Slant == SKFontStyleSlant.Upright)
			{
				return index;
			}
		}

		return -1;
	}

	private static byte[]? ExtractStandalone(SKFontStyleSet styles, int index)
	{
		using var typeface = styles.CreateTypeface(index);
		return typeface is null ? null : SfntFaceExtractor.Extract(typeface);
	}

	private static int WeightOf(byte[]? sfnt)
	{
		if (sfnt is null)
		{
			return -1;
		}

		using var typeface = SKTypeface.FromStream(new MemoryStream(sfnt));
		return typeface?.FontWeight ?? -1;
	}

	private static byte[] BuildCollection(byte[]? first, byte[]? second)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);

		var header = 12 + (2 * 4);
		var firstOffset = (header + 3) & ~3;
		var secondOffset = (firstOffset + first.Length + 3) & ~3;

		var collection = new byte[secondOffset + second.Length];
		var span = collection.AsSpan();
		BinaryPrimitives.WriteUInt32BigEndian(span, CollectionSignature);
		BinaryPrimitives.WriteUInt32BigEndian(span[4..], 0x00010000);
		BinaryPrimitives.WriteUInt32BigEndian(span[8..], 2);
		BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)firstOffset);
		BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)secondOffset);

		PlaceMember(span, first, firstOffset);
		PlaceMember(span, second, secondOffset);
		return collection;
	}

	private static void PlaceMember(Span<byte> collection, byte[] font, int offset)
	{
		font.CopyTo(collection[offset..]);

		// Inside a collection every table directory entry addresses the whole file, not its own member.
		var tableCount = BinaryPrimitives.ReadUInt16BigEndian(collection[(offset + 4)..]);
		for (var table = 0; table < tableCount; table++)
		{
			var entry = collection[(offset + 12 + (table * 16) + 8)..];
			BinaryPrimitives.WriteUInt32BigEndian(entry, BinaryPrimitives.ReadUInt32BigEndian(entry) + (uint)offset);
		}
	}
}

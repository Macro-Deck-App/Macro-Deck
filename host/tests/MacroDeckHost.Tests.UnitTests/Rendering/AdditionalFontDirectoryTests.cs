using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Infrastructure.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class AdditionalFontDirectoryTests
{
	private const string FixtureFamily = "Macro Deck Fixture Sans";
	private const uint NameTag = 0x6E616D65;

	private string _root = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), $"macrodeck-fonts-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_root, recursive: true);
	}

	[Test]
	public void Candidates_WithoutXdgVariables_IncludeTheUserFontDirectoriesButNotTheNativelyScannedOne()
	{
		var directories = LinuxFontDirectories.Candidates(_ => null, "/home/user");

		Assert.That(directories,
			Is.EquivalentTo(new[] { "/home/user/.local/share/fonts", "/home/user/.fonts", "/usr/local/share/fonts" }));
	}

	[Test]
	public void Candidates_HonourXdgDataHomeAndDataDirs()
	{
		var environment = new Dictionary<string, string>
		{
			["XDG_DATA_HOME"] = "/data/home/",
			["XDG_DATA_DIRS"] = "/opt/share/:relative/share:/usr/share//:/opt/share"
		};

		var directories = LinuxFontDirectories.Candidates(environment.GetValueOrDefault, "/home/user");

		Assert.That(directories, Is.EquivalentTo(new[] { "/data/home/fonts", "/home/user/.fonts", "/opt/share/fonts" }));
	}

	[Test]
	public void GetFaces_WithAFontOnlyInAnAdditionalDirectory_ListsItAndServesItsFile()
	{
		var fixture = BuildFixtureFont();
		var nested = Directory.CreateDirectory(Path.Combine(_root, "fonts", "fixture"));
		File.WriteAllBytes(Path.Combine(nested.FullName, "Fixture.TTF"), fixture);

		var catalog = new SkiaFontCatalog([Path.Combine(_root, "fonts")]);
		var face = catalog.GetFaces().SingleOrDefault(candidate => candidate.Family == FixtureFamily);
		Assert.That(face, Is.Not.Null, "a font installed only in an additional directory must be offered");

		var served = catalog.GetFaceFile(face!.FaceId);
		using var reparsed = served is null ? null : SKTypeface.FromStream(new MemoryStream(served));

		Assert.Multiple(() =>
		{
			Assert.That(face.RemoteRenderable, Is.True);
			Assert.That(reparsed?.FamilyName, Is.EqualTo(FixtureFamily));
		});
	}

	[Test]
	public void GetFaces_WithAdditionalDirectories_KeepsSystemFaceIdsAndListsEachFaceOnce()
	{
		var fixture = BuildFixtureFont();
		var installedCopy = ExtractInstalledRegularFace() ?? throw new InvalidOperationException();
		foreach (var name in new[] { "first", "second" })
		{
			var directory = Directory.CreateDirectory(Path.Combine(_root, name));
			File.WriteAllBytes(Path.Combine(directory.FullName, "fixture.ttf"), fixture);
			File.WriteAllBytes(Path.Combine(directory.FullName, "installed.otf"), installedCopy);
		}

		var systemIds = new SkiaFontCatalog([]).GetFaces().Select(face => face.FaceId).ToList();
		var extendedIds = new SkiaFontCatalog([Path.Combine(_root, "first"), Path.Combine(_root, "second")])
			.GetFaces()
			.Select(face => face.FaceId)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(extendedIds.Take(systemIds.Count), Is.EqualTo(systemIds));
			Assert.That(extendedIds, Has.Count.EqualTo(systemIds.Count + 1));
		});
	}

	private static byte[]? ExtractInstalledRegularFace()
	{
		var manager = SKFontManager.Default;
		foreach (var family in manager.FontFamilies.Where(name => !string.IsNullOrWhiteSpace(name)))
		{
			using var styles = manager.GetFontStyles(family);
			for (var index = 0; index < styles.Count; index++)
			{
				using var style = styles[index];
				if (style.Weight != 400 || style.Width != 5 || style.Slant != SKFontStyleSlant.Upright)
				{
					continue;
				}

				using var typeface = styles.CreateTypeface(index);
				if (typeface is not null && SfntFaceExtractor.Extract(typeface) is { } bytes)
				{
					return bytes;
				}
			}
		}

		return null;
	}

	private static byte[] BuildFixtureFont()
	{
		var source = ExtractInstalledRegularFace();
		if (source is null)
		{
			Assert.Ignore("No installed regular face can be extracted to build a fixture font from.");
		}

		var font = WithNameTable(source!, BuildNameTable());
		using var typeface = SKTypeface.FromStream(new MemoryStream(font));
		if (typeface?.FamilyName != FixtureFamily)
		{
			Assert.Ignore("This platform's Skia backend does not read the renamed fixture font.");
		}

		return font;
	}

	private static byte[] BuildNameTable()
	{
		var records = new (ushort NameId, string Value)[]
		{
			(1, FixtureFamily),
			(2, "Regular"),
			(4, $"{FixtureFamily} Regular"),
			(6, "MacroDeckFixtureSans-Regular")
		};
		var strings = records.Select(record => Encoding.BigEndianUnicode.GetBytes(record.Value)).ToArray();

		var headerLength = 6 + (12 * records.Length);
		var table = new byte[headerLength + strings.Sum(value => value.Length)];
		var span = table.AsSpan();
		BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)records.Length);
		BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)headerLength);

		var offset = 0;
		for (var i = 0; i < records.Length; i++)
		{
			var record = span[(6 + (12 * i))..];
			BinaryPrimitives.WriteUInt16BigEndian(record, 3);
			BinaryPrimitives.WriteUInt16BigEndian(record[2..], 1);
			BinaryPrimitives.WriteUInt16BigEndian(record[4..], 0x0409);
			BinaryPrimitives.WriteUInt16BigEndian(record[6..], records[i].NameId);
			BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)strings[i].Length);
			BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)offset);
			strings[i].CopyTo(span[(headerLength + offset)..]);
			offset += strings[i].Length;
		}

		return table;
	}

	private static byte[] WithNameTable(byte[] font, byte[] nameTable)
	{
		var tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
		var appendAt = (font.Length + 3) & ~3;
		var result = new byte[appendAt + nameTable.Length];
		font.CopyTo(result, 0);
		nameTable.CopyTo(result, appendAt);

		for (var i = 0; i < tableCount; i++)
		{
			var entry = result.AsSpan(12 + (16 * i));
			if (BinaryPrimitives.ReadUInt32BigEndian(entry) != NameTag)
			{
				continue;
			}

			BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)appendAt);
			BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)nameTable.Length);
			return result;
		}

		Assert.Ignore("The installed face has no name table to replace.");
		return result;
	}
}

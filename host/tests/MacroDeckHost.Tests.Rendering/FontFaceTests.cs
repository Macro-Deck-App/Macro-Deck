using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HarfBuzzSharp;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Text;
using SkiaSharp;

namespace MacroDeckHost.Tests.Rendering;

public sealed class FontFaceTests
{
	private const string Chinese = "中文字体";

	private static byte[] Anta => File.ReadAllBytes(Path.Combine(Fixtures.RepoRoot,
		Fixtures.Manifest.GetProperty("font").GetString()!));

	private static JsonElement Tree(string text)
	{
		var root = $$$"""
					  {"id":"r","type":"ui.stack","children":[
					  	{"id":"t","type":"ui.text","properties":{"size":{"basis":0.25},"text":"{{{text}}}"}}]}
					  """;
		using var document = JsonDocument.Parse($$"""{"revision":1,"root":{{root}}}""");
		return document.RootElement.Clone();
	}

	private static FontFace ChineseFace(RenderFonts fonts)
	{
		var face = fonts.Faces("ui", "zh").Select(candidate => candidate.Opened).FirstOrDefault();
		if (face is null || !face.Covers(Chinese[0]))
		{
			Assert.Ignore("this machine has no Chinese face installed");
		}

		return face!;
	}

	[Test]
	public async Task A_chinese_face_draws_with_the_typeface_it_is_shaped_with()
	{
		var fonts = new RenderFonts(null);
		var stacked = ChineseFace(fonts);
		var fallback = fonts.Fallback(Chinese[0], "zh")!.Opened;

		await Assert.MultipleAsync(async () =>
		{
			foreach (var face in new[] { stacked, fallback })
			{
				Assert.That(FamilyNames(face.HarfBuzzFace), Does.Contain(face.DrawingTypeface.FamilyName));
				Assert.That(DrawnGlyphs(face, Chinese), Is.EqualTo(ShapedGlyphs(face, Chinese)));
				Assert.That(DrawnGlyphs(face, Chinese), Has.None.EqualTo(0));
			}

			await Task.CompletedTask;
		});
	}

	[Test]
	public void A_chinese_label_is_drawn_in_a_chinese_culture()
	{
		var fonts = new RenderFonts(null);
		ChineseFace(fonts);
		var renderer = new TileRenderer(fonts);

		var label = renderer.Render(Fixtures.Input(Tree(Chinese), 120, 120, 120) with { Culture = "zh-CN" });
		var blank = renderer.Render(Fixtures.Input(Tree(""), 120, 120, 120) with { Culture = "zh-CN" });

		var inked = label.Pixels.Where((value, index) => value != blank.Pixels[index]).Count();
		Assert.That(inked, Is.GreaterThan(400), "the characters left ink on the key");
	}

	[Test]
	public void A_member_of_a_font_collection_is_shaped_from_that_member()
	{
		using var system = SKTypeface.Default.OpenStream(out _);
		using var systemData = SKData.Create(system);
		var collection = Collection(systemData.ToArray(), Anta);
		using var member = SKTypeface.FromData(SKData.CreateCopy(collection), 1);
		if (member is null)
		{
			Assert.Ignore("this platform's font manager opens no collection member past the first");
		}

		var face = RenderFontFace.FromTypeface(member!)?.Opened;

		Assert.That(face, Is.Not.Null, "the shaping face matches the drawn member");
		Assert.That((face!.CollectionIndex, face.HarfBuzzFace.GlyphCount), Is.EqualTo((1, member!.GlyphCount)));
	}

	[Test]
	public void Every_character_one_installed_font_covers_shares_that_font_s_single_face()
	{
		int[] codepoints =
		[
			.. Enumerable.Range(0x4E00, 48), .. Enumerable.Range(0x1F600, 24), .. Enumerable.Range(0x3041, 16)
		];
		var first = new RenderFonts(null);
		var second = new RenderFonts(null);

		var faces = codepoints
			.SelectMany(codepoint => new[] { first.Fallback(codepoint, "zh"), second.Fallback(codepoint, "ja") })
			.OfType<RenderFontFace>()
			.ToList();
		if (faces.Count == 0)
		{
			Assert.Ignore("this machine has no fallback face for these characters");
		}

		var perFamily = faces.GroupBy(face => face.Opened.DrawingTypeface.FamilyName)
			.ToDictionary(group => group.Key, group => group.Distinct().Count());
		Assert.That(perFamily.Values, Is.All.EqualTo(1), string.Join(", ", perFamily.Keys));
	}

	[Test]
	public async Task The_same_tree_drawn_on_several_threads_at_once_comes_out_identical()
	{
		var fonts = new RenderFonts(new RenderFontFace("Anta", Anta));
		var renderer = new TileRenderer(fonts);
		var input = Fixtures.Input(Tree("Volume 音量 Ω 😀 ٣"), 120, 120, 120) with { Fonts = fonts };
		const int threads = 8;
		using var start = new Barrier(threads);

		var renders = await Task.WhenAll(Enumerable.Range(0, threads)
			.Select(_ => Task.Factory.StartNew(() =>
				{
					start.SignalAndWait();
					return Enumerable.Range(0, 4).Select(_ => renderer.Render(input).Pixels).ToList();
				},
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default)));

		var expected = new TileRenderer(fonts).Render(input).Pixels;
		Assert.That(renders.SelectMany(pixels => pixels), Has.All.EqualTo(expected));
	}

	private static uint[] ShapedGlyphs(FontFace face, string text)
	{
		return text.Select(character => face.HarfBuzzFont.TryGetGlyph(character, out var glyph) ? glyph : 0u)
			.ToArray();
	}

	private static uint[] DrawnGlyphs(FontFace face, string text)
	{
		using var font = new SKFont(face.DrawingTypeface);
		return font.GetGlyphs(text).Select(glyph => (uint)glyph).ToArray();
	}

	private static HashSet<string> FamilyNames(Face face)
	{
		using var table = face.ReferenceTable(Tag.Parse("name"));
		var data = table.AsSpan();
		var names = new HashSet<string>(StringComparer.Ordinal);
		var count = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
		var storage = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
		for (var index = 0; index < count; index++)
		{
			var record = data[(6 + index * 12)..];
			var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
			var nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
			var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
			var offset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
			if (nameId is not (1 or 16))
			{
				continue;
			}

			var bytes = data.Slice(storage + offset, length);
			names.Add(platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes));
		}

		return names;
	}

	private static byte[] Collection(params byte[][] fonts)
	{
		var header = 12 + 4 * fonts.Length;
		var bases = new int[fonts.Length];
		var size = header;
		for (var index = 0; index < fonts.Length; index++)
		{
			bases[index] = size;
			size += fonts[index].Length + 3 & ~3;
		}

		var collection = new byte[size];
		"ttcf"u8.CopyTo(collection);
		BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x00010000);
		BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), (uint)fonts.Length);
		for (var index = 0; index < fonts.Length; index++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12 + 4 * index), (uint)bases[index]);
			var font = collection.AsSpan(bases[index], fonts[index].Length);
			fonts[index].CopyTo(font);
			var tables = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);
			for (var table = 0; table < tables; table++)
			{
				var offset = font[(12 + table * 16 + 8)..];
				BinaryPrimitives.WriteUInt32BigEndian(offset,
					BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)bases[index]);
			}
		}

		return collection;
	}
}

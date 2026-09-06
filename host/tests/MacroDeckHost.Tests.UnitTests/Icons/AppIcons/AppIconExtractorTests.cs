using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Icons.AppIcons;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class AppIconExtractorTests
{
	private string _root = null!;
	private AppIconExtractor _extractor = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_extractor = new AppIconExtractor(new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}

	[Test]
	public void CanExtract_AcceptsImagesContainersApplicationsAndShortcuts()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_extractor.CanExtract("logo.png"), Is.True);
			Assert.That(_extractor.CanExtract("app.ico"), Is.True);
			Assert.That(_extractor.CanExtract("app.icns"), Is.True);
			Assert.That(_extractor.CanExtract("app.exe"), Is.True);
			Assert.That(_extractor.CanExtract("app.lnk"), Is.True);
			Assert.That(_extractor.CanExtract("app.desktop"), Is.True);
			Assert.That(_extractor.CanExtract("/Applications/Spotify.app"), Is.True);
			Assert.That(_extractor.CanExtract("/Applications/Spotify.app/"), Is.True);
			Assert.That(_extractor.CanExtract("notes.txt"), Is.False);
			Assert.That(_extractor.CanExtract("icons.zip"), Is.False);
		});
	}

	[Test]
	public async Task Extract_PlainImage_PassesTheBytesThroughUnchanged()
	{
		var png = await CreatePng(64);
		var path = Write("Logo.png", png);

		var result = await _extractor.Extract(path, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Content, Is.EqualTo(png));
			Assert.That(result.Data!.FileName, Is.EqualTo("Logo.png"));
		});
	}

	[Test]
	public async Task Extract_Svg_StaysVectorAndKeepsItsExtension()
	{
		var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"></svg>"u8.ToArray();
		var path = Write("Vector.svg", svg);

		var result = await _extractor.Extract(path, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data!.FileName, Is.EqualTo("Vector.svg"));
	}

	[Test]
	public async Task Extract_IcnsSuite_ReturnsThePngOfTheLargestEntry()
	{
		var large = await CreatePng(256);
		var path = Write("AppIcon.icns", Icns(("ic07", await CreatePng(128)), ("ic08", large)));

		var result = await _extractor.Extract(path, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Content, Is.EqualTo(large));
			Assert.That(result.Data!.FileName, Is.EqualTo("AppIcon.png"));
		});
	}

	[Test]
	public async Task Extract_AppBundle_ResolvesTheIconAndNamesItAfterTheBundle()
	{
		var bundle = Path.Combine(_root, "Spotify.app");
		var resources = Path.Combine(bundle, "Contents", "Resources");
		Directory.CreateDirectory(resources);
		await File.WriteAllTextAsync(Path.Combine(bundle, "Contents", "Info.plist"),
			"<plist><dict><key>CFBundleIconFile</key><string>AppIcon</string></dict></plist>");
		await File.WriteAllBytesAsync(Path.Combine(resources, "AppIcon.icns"),
			Icns(("ic08", await CreatePng(256))));

		var result = await _extractor.Extract(bundle, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data!.FileName, Is.EqualTo("Spotify.png"));
	}

	[Test]
	public async Task Extract_AppBundleWithoutAnyIcon_ReportsUnsupported()
	{
		var bundle = Path.Combine(_root, "Empty.app");
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));

		var result = await _extractor.Extract(bundle, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task Extract_PlainDirectory_ReportsUnsupported()
	{
		var directory = Path.Combine(_root, "Pictures");
		Directory.CreateDirectory(directory);

		var result = await _extractor.Extract(directory, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task Extract_MissingPath_ReportsUnsupported()
	{
		var result = await _extractor.Extract(Path.Combine(_root, "gone.png"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task Extract_UndecodableContainer_ReportsUnsupported()
	{
		var path = Write("broken.icns", "not an icon suite"u8.ToArray());

		var result = await _extractor.Extract(path, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task ExtractFromContent_Icns_DecodesTheLargestEntry()
	{
		var large = await CreatePng(256);
		var bytes = Icns(("ic07", await CreatePng(128)), ("ic08", large));

		var result = await _extractor.ExtractFromContent("AppIcon.icns",
			new MemoryStream(bytes),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Content, Is.EqualTo(large));
			Assert.That(result.Data!.FileName, Is.EqualTo("AppIcon.png"));
		});
	}

	[Test]
	public async Task ExtractFromContent_Ico_DecodesToPng()
	{
		var frame = await CreatePng(32);
		var bytes = BuildIco(frame);

		var result = await _extractor.ExtractFromContent("App.ico", new MemoryStream(bytes), CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data!.FileName, Is.EqualTo("App.png"));
	}

	[Test]
	public async Task ExtractFromContent_ForwardOnlyStream_IsConsumedWithoutSeekingOrReadingLength()
	{
		var frame = await CreatePng(32);
		var bytes = BuildIco(frame);

		var result = await _extractor.ExtractFromContent("App.ico",
			new ForwardOnlyStream(bytes),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data!.FileName, Is.EqualTo("App.png"));
	}

	[Test]
	public async Task ExtractFromContent_OversizedUpload_IsRefusedRatherThanTruncated()
	{
		var result = await _extractor.ExtractFromContent("App.ico",
			new ZeroStream(65L * 1024 * 1024),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.ValidationError));
		});
	}

	[Test]
	public async Task ExtractFromContent_ExecutableWithoutAnIcon_ReportsUnsupported()
	{
		var result = await _extractor.ExtractFromContent("Tool.exe",
			new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public void CanExtractContent_AcceptsOnlyContainers()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_extractor.CanExtractContent("app.ico"), Is.True);
			Assert.That(_extractor.CanExtractContent("app.icns"), Is.True);
			Assert.That(_extractor.CanExtractContent("app.exe"), Is.True);
			Assert.That(_extractor.CanExtractContent("app.dll"), Is.True);
			Assert.That(_extractor.CanExtractContent("app.lnk"), Is.False);
			Assert.That(_extractor.CanExtractContent("app.url"), Is.False);
			Assert.That(_extractor.CanExtractContent("app.desktop"), Is.False);
			Assert.That(_extractor.CanExtractContent("logo.png"), Is.False);
			Assert.That(_extractor.CanExtractContent("notes.txt"), Is.False);
		});
	}

	private static byte[] BuildIco(byte[] framePng)
	{
		using var stream = new MemoryStream();
		WriteUInt16LittleEndian(stream, 0);
		WriteUInt16LittleEndian(stream, 1);
		WriteUInt16LittleEndian(stream, 1);

		var offset = 6 + DirectoryEntryLength;
		stream.WriteByte(32);
		stream.WriteByte(32);
		stream.WriteByte(0);
		stream.WriteByte(0);
		WriteUInt16LittleEndian(stream, 1);
		WriteUInt16LittleEndian(stream, 32);
		WriteUInt32LittleEndian(stream, (uint)framePng.Length);
		WriteUInt32LittleEndian(stream, (uint)offset);
		stream.Write(framePng);
		return stream.ToArray();
	}

	private static void WriteUInt16LittleEndian(Stream stream, ushort value)
	{
		Span<byte> buffer = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt32LittleEndian(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
		stream.Write(buffer);
	}

	private const int DirectoryEntryLength = 16;

	private static byte[] Icns(params (string Type, byte[] Payload)[] entries)
	{
		using var buffer = new MemoryStream();
		buffer.Write("icns"u8);
		buffer.Write(new byte[4]);
		foreach (var (type, payload) in entries)
		{
			buffer.Write(Encoding.ASCII.GetBytes(type));
			var length = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(length, (uint)(payload.Length + 8));
			buffer.Write(length);
			buffer.Write(payload);
		}

		var bytes = buffer.ToArray();
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
		return bytes;
	}

	private static async Task<byte[]> CreatePng(int size)
	{
		using var image = new Image<Rgba32>(size, size, new Rgba32(0, 128, 255, 255));
		using var stream = new MemoryStream();
		await image.SaveAsPngAsync(stream);
		return stream.ToArray();
	}

	private string Write(string fileName, byte[] content)
	{
		var path = Path.Combine(_root, fileName);
		File.WriteAllBytes(path, content);
		return path;
	}

	private sealed class ForwardOnlyStream : Stream
	{
		private readonly MemoryStream _inner;

		public ForwardOnlyStream(byte[] content) => _inner = new MemoryStream(content);

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> _inner.ReadAsync(buffer, offset, count, cancellationToken);

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			=> _inner.ReadAsync(buffer, cancellationToken);

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush()
		{
		}
	}

	private sealed class ZeroStream : Stream
	{
		private readonly long _length;
		private long _read;

		public ZeroStream(long length) => _length = length;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var take = (int)Math.Min(count, _length - _read);
			if (take <= 0)
			{
				return 0;
			}

			Array.Clear(buffer, offset, take);
			_read += take;
			return take;
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush()
		{
		}
	}
}

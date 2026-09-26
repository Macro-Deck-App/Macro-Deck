using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconVariantDerivationTests
{
	private static readonly Rgba32 _red = new(220, 20, 20);
	private static readonly Rgba32 _green = new(20, 220, 20);
	private static readonly Rgba32 _blue = new(20, 20, 220);

	private IconTestHarness _harness = null!;
	private IconService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.VariantDeriver,
			_harness.Coalescer,
			_harness.Mediator,
			new IconPackOwnerRegistry([]));
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task A_master_only_pack_serves_a_size_variant_made_from_its_master_and_keeps_it()
	{
		var icon = await Install(new ArchiveIcon("star", Webp(1024, _red)));

		var first = await Get(icon, 128);
		var second = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(first.Edge, Is.EqualTo(128));
			Assert.That(first.Color, Is.EqualTo(_red).Using<Rgba32>(Close));
			Assert.That(first.ETag, Does.EndWith("-128\""));
			Assert.That(first.Version, Is.EqualTo(icon.MasterContentHash));
			Assert.That(second.Bytes, Is.EqualTo(first.Bytes));
			Assert.That(_harness.Storage.ListVariants(icon.PackId, icon.Id), Has.Some.StartsWith("128-"));
		});
	}

	[Test]
	public async Task Size_variants_an_older_export_carried_are_ignored_in_favour_of_the_master()
	{
		var icon = await Install(new ArchiveIcon("star", Webp(1024, _red), [(128, Webp(128, _green))]));

		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Edge, Is.EqualTo(128));
			Assert.That(served.Color, Is.EqualTo(_red).Using<Rgba32>(Close));
			Assert.That(icon.AvailableSizes, Is.Empty);
			Assert.That(_harness.Storage.ListVariants(icon.PackId, icon.Id), Has.None.EqualTo("128"));
		});
	}

	[Test]
	public async Task An_installed_icon_keeps_serving_its_own_size_variant_byte_for_byte()
	{
		var pack = await _harness.CreatePack();
		var plain = Webp(128, _green);
		var icon = await AddInstalledIcon(pack, Webp(1024, _red), plain, [128, 256, 512]);

		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Bytes, Is.EqualTo(plain));
			Assert.That(served.ETag, Does.EndWith("-128\""));
			Assert.That(_harness.Cache.GetIconById(icon.Id)!.AvailableSizes, Is.EqualTo(new[] { 128, 256, 512 }));
			Assert.That(_harness.Storage.ListVariants(icon.PackId, icon.Id), Has.None.StartsWith("128-"));
		});
	}

	[Test]
	public async Task A_leftover_size_file_that_the_icon_does_not_list_is_never_served()
	{
		var pack = await _harness.CreatePack();
		var icon = await AddInstalledIcon(pack, Webp(1024, _red), Webp(128, _green), sizes: []);

		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Edge, Is.EqualTo(128));
			Assert.That(served.Color, Is.EqualTo(_red).Using<Rgba32>(Close));
		});
	}

	[Test]
	public async Task A_master_no_larger_than_the_requested_size_is_served_as_it_is()
	{
		var master = Webp(100, _red);
		var icon = await Install(new ArchiveIcon("small", master));

		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Bytes, Is.EqualTo(master));
			Assert.That(served.ETag, Does.EndWith("-master\""));
			Assert.That(served.Version, Is.EqualTo(icon.MasterContentHash));
			Assert.That(_harness.Storage.ListVariants(icon.PackId, icon.Id), Is.EqualTo(new[] { IconVariants.Master }));
		});
	}

	[Test]
	public async Task A_master_larger_than_hosts_write_still_gets_size_variants()
	{
		var icon = await Install(new ArchiveIcon("huge", Webp(2048, _red)));

		var served = await Get(icon, 128);

		Assert.That(served.Edge, Is.EqualTo(128));
	}

	[Test]
	public async Task A_master_that_would_decode_beyond_the_pixel_budget_is_served_as_it_is_and_does_not_stop_other_icons()
	{
		using var deriver = new ImageSharpIconVariantDeriver(_harness.Storage,
			_harness.Cache,
			TimeProvider.System,
			_harness.Logger,
			maxDecodedPixels: 600_000);
		_service = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			deriver,
			_harness.Coalescer,
			_harness.Mediator,
			new IconPackOwnerRegistry([]));
		var longAnimation = Webp(512, _red, frames: 4);
		var animated = await Install(new ArchiveIcon("animated", longAnimation));
		var still = await Install(new ArchiveIcon("still", Webp(512, _blue)));

		var animatedServed = await Get(animated, 128);
		var stillServed = await Get(still, 128);

		Assert.Multiple(() =>
		{
			Assert.That(animatedServed.Bytes, Is.EqualTo(longAnimation));
			Assert.That(animatedServed.Version, Is.EqualTo(animated.MasterContentHash));
			Assert.That(stillServed.Edge, Is.EqualTo(128));
		});
	}

	[Test]
	public async Task A_master_that_cannot_be_decoded_is_served_without_a_version_so_no_client_keeps_it()
	{
		var pack = await _harness.CreatePack();
		var broken = "not an image"u8.ToArray();
		var icon = await AddInstalledIcon(pack, broken, plain: null, sizes: []);

		var result = await _service.GetImage(icon.Id, 128, acceptWebp: true, staticFrame: false, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		await using var content = result.Data!.Content;
		Assert.Multiple(() =>
		{
			Assert.That(ReadAll(content), Is.EqualTo(broken));
			Assert.That(result.Data.Version, Is.Null);
		});
	}

	[Test]
	public async Task An_animated_master_keeps_its_frames_in_the_size_variant()
	{
		var icon = await Install(new ArchiveIcon("spinner", Webp(512, _red, frames: 4)));

		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Edge, Is.EqualTo(128));
			Assert.That(served.Frames, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task A_client_without_webp_gets_the_size_variant_as_png()
	{
		var icon = await Install(new ArchiveIcon("star", Webp(1024, _red)));

		var result = await _service.GetImage(icon.Id, 128, acceptWebp: false, staticFrame: false, CancellationToken.None);

		await using var content = result.Data!.Content;
		using var image = Image.Load<Rgba32>(ReadAll(content));
		Assert.Multiple(() =>
		{
			Assert.That(result.Data.ContentType, Is.EqualTo("image/png"));
			Assert.That(image.Width, Is.EqualTo(128));
		});
	}

	[Test]
	public async Task Upgrading_to_a_new_master_serves_sizes_of_the_new_master()
	{
		var icon = await Install(new ArchiveIcon("star", Webp(1024, _red)));
		await SimulateVariantsFromAnOlderHost(icon, Webp(128, _green));
		var oldToken = IconVariants.MasterToken(icon.MasterContentHash!);
		await Get(icon, 256);

		var result = await _harness.RestoreService.UpgradePack(icon.PackId,
			"v2.macroDeckIconPack",
			new MemoryStream(Archive(new ArchiveIcon("star", Webp(1024, _blue), FixedId: icon.SourceIconId))),
			CancellationToken.None);
		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(served.Color, Is.EqualTo(_blue).Using<Rgba32>(Close));
			Assert.That(_harness.Cache.GetIconById(icon.Id)!.AvailableSizes, Is.Empty);
			Assert.That(_harness.Storage.ListVariants(icon.PackId, icon.Id), Has.None.EndsWith(oldToken));
		});
	}

	[Test]
	public async Task Upgrading_with_an_unchanged_master_keeps_the_installed_size_variants()
	{
		var master = Webp(1024, _red);
		var icon = await Install(new ArchiveIcon("star", master));
		var plain = Webp(128, _green);
		await SimulateVariantsFromAnOlderHost(icon, plain);

		await _harness.RestoreService.UpgradePack(icon.PackId,
			"v2.macroDeckIconPack",
			new MemoryStream(Archive(new ArchiveIcon("star", master, FixedId: icon.SourceIconId))),
			CancellationToken.None);
		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(served.Bytes, Is.EqualTo(plain));
			Assert.That(_harness.Cache.GetIconById(icon.Id)!.AvailableSizes, Is.EqualTo(new[] { 128 }));
		});
	}

	[Test]
	public async Task An_upgrade_whose_master_fails_its_declared_hash_leaves_the_installed_icon_alone()
	{
		var master = Webp(1024, _red);
		var icon = await Install(new ArchiveIcon("star", master));
		var plain = Webp(128, _green);
		await SimulateVariantsFromAnOlderHost(icon, plain);

		await _harness.RestoreService.UpgradePack(icon.PackId,
			"v2.macroDeckIconPack",
			new MemoryStream(Archive(new ArchiveIcon("star",
				Webp(1024, _blue),
				FixedId: icon.SourceIconId,
				DeclaredMasterHash: MasterContentHash.Compute("something else"u8).Value))),
			CancellationToken.None);
		var served = await Get(icon, 128);
		await using var onDisk = _harness.Storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master)!;

		Assert.Multiple(() =>
		{
			Assert.That(served.Bytes, Is.EqualTo(plain));
			Assert.That(ReadAll(onDisk), Is.EqualTo(master));
			Assert.That(_harness.Cache.GetIconById(icon.Id)!.AvailableSizes, Is.EqualTo(new[] { 128 }));
		});
	}

	[Test]
	public async Task A_plugin_pack_update_with_a_new_master_never_serves_the_old_size_variant()
	{
		var icon = await Install(new ArchiveIcon("star", Webp(1024, _red)));
		await SimulateVariantsFromAnOlderHost(icon, Webp(128, _green));

		var result = await _harness.RestoreService.ReplaceFromSource(icon.PackId,
			"logos.macroDeckIconPack",
			new MemoryStream(Archive(new ArchiveIcon("star", Webp(1024, _blue)))),
			new IconPackSourceStamp(IconPackSourceType.Plugin, "com.example.logos/logos", "sha256:" + new string('b', 64)),
			new HashSet<Guid>(),
			CancellationToken.None);
		var served = await Get(icon, 128);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(served.Color, Is.EqualTo(_blue).Using<Rgba32>(Close));
		});
	}

	private async Task<IconEntity> Install(ArchiveIcon icon)
	{
		var result = await _harness.RestoreService.RestoreAsNewPack("pack.macroDeckIconPack",
			new MemoryStream(Archive(icon)),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return _harness.Cache.GetIconsByPackId(result.Data!.Id).Single();
	}

	private async Task<IconEntity> AddInstalledIcon(IconPackEntity pack, byte[] master, byte[]? plain, int[] sizes)
	{
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "installed",
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = sizes,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Storage.WriteVariant(pack.Id, icon.Id, IconVariants.Master, master, CancellationToken.None);
		if (plain is not null)
		{
			await _harness.Storage.WriteVariant(pack.Id, icon.Id, "128", plain, CancellationToken.None);
		}

		return icon;
	}

	private async Task SimulateVariantsFromAnOlderHost(IconEntity icon, byte[] plain)
	{
		await _harness.Storage.WriteVariant(icon.PackId, icon.Id, "128", plain, CancellationToken.None);
		icon.AvailableSizes = [128];
		await _harness.Cache.UpdateIcon(icon);
	}

	private async Task<Served> Get(IconEntity icon, int size)
	{
		var result = await _service.GetImage(icon.Id, size, acceptWebp: true, staticFrame: false, CancellationToken.None);
		Assert.That(result.Success, Is.True);
		await using var content = result.Data!.Content;
		var bytes = ReadAll(content);
		using var image = Image.Load<Rgba32>(bytes);
		return new Served(bytes,
			Math.Max(image.Width, image.Height),
			image.Frames.Count,
			image[image.Width / 2, image.Height / 2],
			result.Data.ETag,
			result.Data.Version);
	}

	private static byte[] Archive(params ArchiveIcon[] icons)
	{
		var entries = icons.Select(icon => new
		{
			icon.Id,
			icon.Name,
			State = "Ready",
			AvailableSizes = icon.Variants.Select(variant => variant.Size).ToArray(),
			MasterContentHash = icon.DeclaredMasterHash ?? MasterContentHash.Compute(icon.Master).Value
		});
		var manifest = JsonSerializer.Serialize(new { Id = Guid.CreateVersion7(), Name = "Pack", Icons = entries });

		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			Write(zip, "pack.json", Encoding.UTF8.GetBytes(manifest));
			foreach (var icon in icons)
			{
				Write(zip, $"icons/{icon.Id}/master.webp", icon.Master);
				foreach (var (size, bytes) in icon.Variants)
				{
					Write(zip, $"icons/{icon.Id}/{size}.webp", bytes);
				}
			}
		}

		return stream.ToArray();
	}

	private static void Write(ZipArchive zip, string name, byte[] content)
	{
		using var entry = zip.CreateEntry(name).Open();
		entry.Write(content);
	}

	private static byte[] Webp(int edge, Rgba32 color, int frames = 1)
	{
		using var image = new Image<Rgba32>(edge, edge, color);
		for (var index = 1; index < frames; index++)
		{
			using var frame = new Image<Rgba32>(edge, edge, index % 2 == 0 ? color : _green);
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		using var stream = new MemoryStream();
		image.SaveAsWebp(stream, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 90 });
		return stream.ToArray();
	}

	private static byte[] ReadAll(Stream stream)
	{
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static bool Close(Rgba32 actual, Rgba32 expected)
		=> Math.Abs(actual.R - expected.R) < 40 && Math.Abs(actual.G - expected.G) < 40 && Math.Abs(actual.B - expected.B) < 40;

	private sealed record ArchiveIcon(
		string Name,
		byte[] Master,
		(int Size, byte[] Bytes)[]? SizeVariants = null,
		Guid? FixedId = null,
		string? DeclaredMasterHash = null)
	{
		public Guid Id { get; } = FixedId ?? Guid.CreateVersion7();

		public (int Size, byte[] Bytes)[] Variants => SizeVariants ?? [];
	}

	private sealed record Served(byte[] Bytes, int Edge, int Frames, Rgba32 Color, string ETag, string? Version);
}

using System.Text;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconServiceTests
{
	private static readonly string[] _expectedRemainingIconNames = ["c"];

	private IconTestHarness _harness = null!;
	private IconService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.Coalescer,
			_harness.Mediator);
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Rename_UpdatesIconAndPublishes()
	{
		var (pack, icon) = await AddIcon();

		var result = await _service.Rename(icon.Id, "  new name  ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconById(icon.Id)!.Name, Is.EqualTo("new name"));
			Assert.That(_harness.Mediator.Published.OfType<IconUpdatedNotification>().Count(), Is.EqualTo(1));
			Assert.That(pack.Id, Is.EqualTo(icon.PackId));
		});
	}

	[Test]
	public async Task Rename_InReadOnlyPack_IsRejected()
	{
		var (_, icon) = await AddIcon(isReadOnly: true);

		var result = await _service.Rename(icon.Id, "x");

		Assert.That(result.Error, Is.EqualTo(IconError.PackReadOnly));
	}

	[Test]
	public async Task Delete_RemovesIconAndFiles()
	{
		var (pack, icon) = await AddIcon();
		await _harness.Storage.WriteVariant(pack.Id,
			icon.Id,
			IconVariants.Master,
			new byte[] { 1 },
			CancellationToken.None);

		var result = await _service.Delete(icon.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconById(icon.Id), Is.Null);
			Assert.That(_harness.Storage.OpenVariant(pack.Id, icon.Id, IconVariants.Master), Is.Null);
			Assert.That(_harness.Mediator.Published.OfType<IconDeletedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task DeleteMany_RemovesAllIconsAndFiles_SkippingUnknownIds()
	{
		var pack = await _harness.CreatePack();
		var icons = new[] { NewIcon(pack.Id, "a"), NewIcon(pack.Id, "b"), NewIcon(pack.Id, "c") };
		await _harness.Cache.AddIcons(pack.Id, icons);
		foreach (var icon in icons)
		{
			await _harness.Storage.WriteVariant(pack.Id,
				icon.Id,
				IconVariants.Master,
				new byte[] { 1 },
				CancellationToken.None);
		}

		var result = await _service.DeleteMany([icons[0].Id, icons[1].Id, Guid.NewGuid()]);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data, Is.EqualTo(2));
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.Name),
				Is.EqualTo(_expectedRemainingIconNames));
			Assert.That(_harness.Storage.OpenVariant(pack.Id, icons[0].Id, IconVariants.Master), Is.Null);
			Assert.That(_harness.Storage.OpenVariant(pack.Id, icons[2].Id, IconVariants.Master), Is.Not.Null);
			Assert.That(_harness.Mediator.Published.OfType<IconDeletedNotification>().Count(), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task DeleteMany_AnyIconInReadOnlyPack_RejectsWholeOperation()
	{
		var pack = await _harness.CreatePack();
		var readOnlyPack = await _harness.CreatePack("Store Pack", isReadOnly: true);
		var normal = NewIcon(pack.Id, "normal");
		var locked = NewIcon(readOnlyPack.Id, "locked");
		await _harness.Cache.AddIcons(pack.Id, [normal]);
		await _harness.Cache.AddIcons(readOnlyPack.Id, [locked]);

		var result = await _service.DeleteMany([normal.Id, locked.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.PackReadOnly));
			Assert.That(_harness.Cache.GetIconById(normal.Id), Is.Not.Null, "nothing must be deleted");
		});
	}

	private static IconEntity NewIcon(Guid packId, string name)
		=> new()
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			Name = name,
			CreatedAt = DateTime.UtcNow
		};

	[Test]
	public async Task GetImage_NotReadyIcon_ReturnsNotReady()
	{
		var (_, icon) = await AddIcon();

		var result = await _service.GetImage(icon.Id,
			128,
			acceptWebp: true,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.NotReady));
	}

	[Test]
	public async Task GetImage_FallsBackToClosestAvailableSize()
	{
		var (pack, icon) = await AddIcon();
		icon.ProcessingState = IconProcessingState.Ready;
		icon.AvailableSizes = [256];
		icon.MasterContentHash = MasterContentHash.Compute("abc"u8).Value;
		await _harness.Cache.UpdateIcon(icon);
		await _harness.Storage.WriteVariant(pack.Id, icon.Id, "256", new byte[] { 42 }, CancellationToken.None);
		await _harness.Storage.WriteVariant(pack.Id,
			icon.Id,
			IconVariants.Master,
			new byte[] { 7 },
			CancellationToken.None);

		var result = await _service.GetImage(icon.Id,
			128,
			acceptWebp: true,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		await using var stream = result.Data!.Content;
		Assert.Multiple(() =>
		{
			Assert.That(stream.ReadByte(), Is.EqualTo(42), "128 is unavailable; the 256 variant is closest");
			Assert.That(result.Data!.ETag, Does.Contain(MasterContentHash.Compute("abc"u8).Value));
			Assert.That(result.Data!.ContentType, Is.EqualTo("image/webp"));
		});
	}

	[Test]
	public async Task GetImage_UnknownIcon_ReturnsNotFound()
	{
		var result = await _service.GetImage(Guid.NewGuid(),
			null,
			acceptWebp: true,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.NotFound));
	}

	[Test]
	public async Task GetImage_WithoutWebpSupport_ServesPngTranscode()
	{
		var (pack, icon) = await AddReadyIconWithWebpMaster();

		var result = await _service.GetImage(icon.Id,
			null,
			acceptWebp: false,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		await using var stream = result.Data!.Content;
		var header = new byte[8];
		await stream.ReadExactlyAsync(header);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.ContentType, Is.EqualTo("image/png"));
			Assert.That(header[1], Is.EqualTo((byte)'P'), "content must be a PNG, not the stored WebP");
			Assert.That(header[2], Is.EqualTo((byte)'N'));
			Assert.That(result.Data!.ETag, Does.Contain(".png"), "ETag must differ from the WebP ETag");
			Assert.That(pack.Id, Is.EqualTo(icon.PackId));
		});
	}

	[Test]
	public async Task GetImage_WithoutWebpSupport_AnimatedIcon_ServesGifTranscode()
	{
		var (_, icon) = await AddReadyIconWithWebpMaster(animated: true);

		var result = await _service.GetImage(icon.Id,
			null,
			acceptWebp: false,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		await using var stream = result.Data!.Content;
		var header = new byte[4];
		await stream.ReadExactlyAsync(header);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.ContentType, Is.EqualTo("image/gif"));
			Assert.That(Encoding.ASCII.GetString(header), Is.EqualTo("GIF8"));
		});
	}

	[Test]
	public async Task GetImage_WithoutWebpSupport_ReusesCachedTranscode()
	{
		var (_, icon) = await AddReadyIconWithWebpMaster();

		var first = await _service.GetImage(icon.Id,
			null,
			acceptWebp: false,
			staticFrame: false,
			CancellationToken.None);
		await first.Data!.Content.DisposeAsync();

		var cacheFile = Directory
			.EnumerateFiles(Path.Combine(_harness.Paths.IconsDirectory, "fallback-cache"))
			.Single();
		var writtenAt = File.GetLastWriteTimeUtc(cacheFile);

		var second = await _service.GetImage(icon.Id,
			null,
			acceptWebp: false,
			staticFrame: false,
			CancellationToken.None);
		await second.Data!.Content.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(second.Success, Is.True);
			Assert.That(File.GetLastWriteTimeUtc(cacheFile),
				Is.EqualTo(writtenAt),
				"the second request must be served from the cached file, not re-transcoded");
		});
	}

	private async Task<(IconPackEntity Pack, IconEntity Icon)> AddReadyIconWithWebpMaster(bool animated = false)
	{
		var (pack, icon) = await AddIcon();
		icon.ProcessingState = IconProcessingState.Ready;
		icon.IsAnimated = animated;
		icon.MasterContentHash = MasterContentHash.Compute("abc"u8).Value;
		await _harness.Cache.UpdateIcon(icon);

		using var image = new Image<Rgba32>(4, 4);
		if (animated)
		{
			using var frame = new Image<Rgba32>(4, 4);
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		using var buffer = new MemoryStream();
		await image.SaveAsWebpAsync(buffer);
		await _harness.Storage.WriteVariant(pack.Id,
			icon.Id,
			IconVariants.Master,
			buffer.ToArray(),
			CancellationToken.None);
		return (pack, icon);
	}

	private async Task<(IconPackEntity Pack, IconEntity Icon)> AddIcon(bool isReadOnly = false)
	{
		var pack = await _harness.CreatePack(isReadOnly: isReadOnly);
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "icon",
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		return (pack, icon);
	}
}
